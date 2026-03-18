using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.Services;
using LiteDBEditor;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 主窗口 ViewModel，作为应用程序的核心控制中心。
/// 负责管理数据库连接、集合列表切换、文档数据的分页加载以及全局保存/撤销逻辑。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _windowTitle = "LiteDB Editor";

    [ObservableProperty]
    private string? _currentDbPath;

    [ObservableProperty]
    private string? _currentDbFileName;

    [ObservableProperty]
    private bool _isDatabaseLoaded;

    /// <summary>
    /// 当前数据库中的所有集合（表）名称列表。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _collections = new();

    /// <summary>
    /// 当前选中的集合名称。
    /// </summary>
    [ObservableProperty]
    private string? _selectedCollection;

    /// <summary>
    /// 当前集合绑定的 Schema 源文件路径。
    /// </summary>
    [ObservableProperty]
    private string? _currentBoundCsFilePath;

    /// <summary>
    /// 当前绑定的文件名（用于 UI 显示）。
    /// </summary>
    [ObservableProperty]
    private string? _currentBoundCsFileName;

    /// <summary>
    /// 当前网格中显示的文档包装器集合。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BsonDocumentWrapper> _documents = new();

    /// <summary>
    /// 当前集合所采用的结构定义（Schema）。
    /// </summary>
    [ObservableProperty]
    private SchemaData? _currentSchema;

    /// <summary>
    /// 获取一个值，指示当前是否有任何尚未持久化到数据库的更改（新增、修改或删除）。
    /// </summary>
    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// 显示在主网格下方的错误提示信息。
    /// </summary>
    [ObservableProperty]
    private string? _gridErrorMessage;

    private readonly List<BsonDocumentWrapper> _pendingDeletions = new();
    private CancellationTokenSource? _validationCts;

    /// <summary>
    /// 当 Schema 加载成功或重置时触发。
    /// </summary>
    public event EventHandler<SchemaData?>? SchemaLoaded;

    /// <summary>
    /// 响应集合选中项变更，触发数据加载流程。
    /// </summary>
    partial void OnSelectedCollectionChanged(string? value)
    {
        if (value != null)
        {
            LoadCollectionData(value);
        }
        else
        {
            Documents.Clear();
            CurrentSchema = null;
            CurrentBoundCsFilePath = null;
            CurrentBoundCsFileName = null;
        }
    }

    /// <summary>
    /// 获取系统可用的语言列表。
    /// </summary>
    public Dictionary<string, string> AvailableLanguages => LanguageService.AvailableLanguages;

    /// <summary>
    /// 切换系统语言。
    /// </summary>
    [RelayCommand]
    private void ChangeLanguage(string locale)
    {
        if (locale != LanguageService.CurrentLanguage)
        {
            LanguageService.SetLanguage(locale);
            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    /// <summary>
    /// 获取当前选中的语言标识。
    /// </summary>
    public string SelectedLanguage => LanguageService.CurrentLanguage;

    public MainWindowViewModel()
    {
        // 订阅文档列表变更，以便在手动增删行时实时更新“保存”按钮状态
        Documents.CollectionChanged += (s, e) => CheckUnsavedChanges();
    }

    /// <summary>
    /// 打开指定的 LiteDB 数据库文件并刷新 UI 状态。
    /// </summary>
    /// <param name="filePath">数据库文件全路径</param>
    public void OpenDatabase(string filePath)
    {
        try
        {
            DataCenter.Database.OpenDatabase(filePath);
            IsDatabaseLoaded = true;
            CurrentDbPath = filePath;
            CurrentDbFileName = System.IO.Path.GetFileName(filePath);
            WindowTitle = $"LiteDB Editor - {DataCenter.Database.CurrentDbPath}";

            RefreshCollections();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error opening database: {ex.Message}");
        }
    }

    /// <summary>
    /// 重新从数据库拉取集合名称列表。
    /// </summary>
    private void RefreshCollections()
    {
        if (!IsDatabaseLoaded) return;

        Collections.Clear();
        foreach (var colNames in DataCenter.Database.GetCollectionNames())
        {
            Collections.Add(colNames);
        }
    }

    /// <summary>
    /// 加载并解析指定集合的数据及绑定的 Schema。
    /// </summary>
    /// <param name="collectionName">要加载的集合名</param>
    private void LoadCollectionData(string collectionName)
    {
        try
        {
            Documents.Clear();

            // 默认加载前 100 条数据（目前暂未实现无限滚动分页）
            var rawDocs = DataCenter.Database.GetDocuments(collectionName, 0, 100);

            var parser = new SchemaParserService();
            SchemaData? properties = null;

            if (DataCenter.Database.CurrentDbPath != null)
            {
                // 1. 尝试查询该集合是否已绑定过 .schema.json
                var boundPath = DataCenter.Bindings.GetBoundSchemaFilePath(DataCenter.Database.CurrentDbPath, collectionName);
                if (!string.IsNullOrEmpty(boundPath))
                {
                    CurrentBoundCsFilePath = boundPath;
                    CurrentBoundCsFileName = System.IO.Path.GetFileName(boundPath);
                    
                    // 从元数据文件加载类定义，并将其转换为 UI 渲染树所需的结构
                    var classDef = DataCenter.Metadata.LoadMetadata(boundPath);
                    if (classDef != null)
                    {
                        properties = new SchemaData 
                        { 
                            TargetName = collectionName,
                            Properties = MapClassToProperties(classDef, classDef)
                        };
                    }
                }
                else
                {
                    CurrentBoundCsFilePath = null;
                    CurrentBoundCsFileName = null;
                }
            }

            // 2. 启发式推导：如果没有显式绑定，则通过采样第一条文档来自动推断结构
            if (properties == null && rawDocs.Count > 0)
            {
                properties = parser.ParseFromBsonDocument(collectionName, rawDocs.FirstOrDefault() ?? new BsonDocument());
            }

            CurrentSchema = properties;
            SchemaLoaded?.Invoke(this, properties);

            // 将原始 BsonDocument 包装为支持属性通知的 ViewModel
            foreach (var doc in rawDocs)
            {
                var docViewModel = new BsonDocumentWrapper(doc, d => CheckUnsavedChanges());
                docViewModel.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(BsonDocumentWrapper.IsModified))
                    {
                        CheckUnsavedChanges();
                    }
                };
                Documents.Add(docViewModel);
            }

            CheckUnsavedChanges();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading collection data: {ex.Message}");
        }
    }

    /// <summary>
    /// 为当前选中的集合绑定一个外部 C# 或 Schema 文件。
    /// </summary>
    public void BindSchemaFile(string csFilePath)
    {
        if (string.IsNullOrEmpty(SelectedCollection) || string.IsNullOrEmpty(DataCenter.Database.CurrentDbPath)) return;

        DataCenter.Bindings.BindSchema(DataCenter.Database.CurrentDbPath, SelectedCollection, csFilePath);

        // 绑定后强制重载，以应用新的 UI 列结构和校验规则
        LoadCollectionData(SelectedCollection);
    }

    /// <summary>
    /// 在数据库中创建一个新集合，并可选地立即为其绑定 Schema。
    /// </summary>
    public void CreateCollection(string collectionName, string? boundCsFilePath)
    {
        if (!IsDatabaseLoaded) return;

        DataCenter.Database.CreateCollection(collectionName);

        if (!string.IsNullOrEmpty(boundCsFilePath) && DataCenter.Database.CurrentDbPath != null)
        {
            DataCenter.Bindings.BindSchema(DataCenter.Database.CurrentDbPath, collectionName, boundCsFilePath);
        }

        RefreshCollections();
        SelectedCollection = collectionName;
    }

    /// <summary>
    /// 物理删除当前选中的集合。
    /// </summary>
    public void DeleteCurrentCollection()
    {
        if (!IsDatabaseLoaded || string.IsNullOrEmpty(SelectedCollection)) return;

        DataCenter.Database.DropCollection(SelectedCollection);
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault();
    }

    /// <summary>
    /// 删除指定的集合（通过 ContextMenu 调用）。
    /// </summary>
    [RelayCommand]
    private void DeleteSpecificCollection(string collectionName)
    {
        if (!IsDatabaseLoaded || string.IsNullOrEmpty(collectionName)) return;

        DataCenter.Database.DropCollection(collectionName);
        RefreshCollections();
        if (SelectedCollection == collectionName)
        {
            SelectedCollection = Collections.FirstOrDefault();
        }
    }

    /// <summary>
    /// 清除网格级别的全局错误提示。
    /// </summary>
    public void ClearGridError()
    {
        GridErrorMessage = null;
    }

    /// <summary>
    /// 检查指定的 BsonValue（通常是 ID）是否在当前内存列表或数据库中存在重复。
    /// 用于防止主键冲突。
    /// </summary>
    public bool IsIdDuplicate(BsonValue newVal, BsonDocumentWrapper? exclude = null)
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return false;

        // 检查当前 ObservableCollection 中的内存数据（包括尚未保存到库的新增项）
        foreach (var doc in Documents)
        {
            if (doc == exclude) continue;
            var currentId = doc.GetRawValue("_id");
            if (currentId == newVal) return true;
        }

        return false; 
    }

    /// <summary>
    /// 将文档标记为待删除，并从 UI 列表中立即移除。
    /// 注意：只有在点击“保存”按钮后，才会执行真正的数据库删除操作。
    /// </summary>
    public void MarkDocumentForDeletion(BsonDocumentWrapper doc)
    {
        if (doc == null) return;
        
        if (doc.OriginalId != BsonValue.Null)
        {
            _pendingDeletions.Add(doc);
        }
        Documents.Remove(doc);
        CheckUnsavedChanges();
    }

    /// <summary>
    /// 强制执行单个文档的持久化保存。
    /// 通常在动态属性编辑弹窗确认后调用，以确保弹窗内的修改立即写回库。
    /// </summary>
    public void ForceSaveDocument(BsonDocumentWrapper doc)
    {
        if (string.IsNullOrEmpty(SelectedCollection) || doc == null) return;

        var finalDoc = doc.GetMergedDocument();
        DataCenter.Database.UpsertDocument(SelectedCollection, finalDoc);
        doc.AcceptChanges();
        doc.SyncOriginalId();
        CheckUnsavedChanges();
    }

    /// <summary>
    /// 重命名集合，并自动同步更新绑定关系。
    /// 支持不区分大小写的重命名（通过中转名实现）。
    /// </summary>
    public void RenameCollection(string oldName, string newName)
    {
        if (!IsDatabaseLoaded || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName) return;

        try
        {
            bool success = false;
            // 针对 LiteDB 的处理：如果只是大小写不同，直接重命名可能会失败，需通过临时名中转
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                var tempName = oldName + "_tmp_" + Guid.NewGuid().ToString("N");
                if (DataCenter.Database.RenameCollection(oldName, tempName))
                {
                    success = DataCenter.Database.RenameCollection(tempName, newName);
                }
            }
            else
            {
                success = DataCenter.Database.RenameCollection(oldName, newName);
            }

            if (success)
            {
                if (DataCenter.Database.CurrentDbPath != null)
                {
                    DataCenter.Bindings.RenameBinding(DataCenter.Database.CurrentDbPath, oldName, newName);
                }

                RefreshCollections();
                if (SelectedCollection == oldName)
                {
                    SelectedCollection = newName;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] RenameCollection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 在当前集合末尾新增一个文档行。
    /// </summary>
    [RelayCommand]
    private void AddNewDocument()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        var newDoc = new BsonDocument();
        // 如果存在 Schema，则根据定义初始化默认值，提高编辑体验
        if (CurrentSchema != null)
        {
            foreach (var prop in CurrentSchema.Properties)
            {
                if (prop.Name == "_id") continue;
                newDoc[prop.Name] = BsonValue.Null;
            }
        }

        var wrapper = new BsonDocumentWrapper(newDoc, d => CheckUnsavedChanges());
        wrapper.PropertyChanged += (s, e) => CheckUnsavedChanges();
        
        Documents.Add(wrapper);
        CheckUnsavedChanges();
    }

    /// <summary>
    /// 删除网格中选中的文档。
    /// </summary>
    [RelayCommand]
    private void DeleteDocument(BsonDocumentWrapper doc)
    {
        if (doc == null) return;
        
        // 加入待删除队列，延迟执行
        _pendingDeletions.Add(doc);
        Documents.Remove(doc);
        CheckUnsavedChanges();
    }

    /// <summary>
    /// 检查并更新界面上的“未保存更改”状态标记。
    /// </summary>
    private void CheckUnsavedChanges()
    {
        HasUnsavedChanges = _pendingDeletions.Any() || Documents.Any(d => d.IsModified || d.OriginalId == LiteDB.BsonValue.Null);
    }

    /// <summary>
    /// 提交所有挂起的更改（新增、修改、删除）到数据库。
    /// 该操作为批量执行。
    /// </summary>
    [RelayCommand]
    private void SaveChanges()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        try
        {
            // 1. 执行逻辑物理删除
            foreach (var deleted in _pendingDeletions)
            {
                if (deleted.OriginalId != LiteDB.BsonValue.Null)
                {
                    DataCenter.Database.DeleteDocument(SelectedCollection, deleted.OriginalId);
                }
            }
            _pendingDeletions.Clear();

            // 2. 处理编辑和新增的数据
            foreach (var doc in Documents)
            {
                if (doc.IsModified || doc.OriginalId == LiteDB.BsonValue.Null)
                {
                    var finalDoc = doc.GetMergedDocument();

                    // 处理 ID 变更：如果用户修改了主键，需要先删除旧键，再插入新键
                    var currentId = finalDoc["_id"];
                    if (doc.OriginalId != LiteDB.BsonValue.Null && doc.OriginalId != currentId)
                    {
                        DataCenter.Database.DeleteDocument(SelectedCollection, doc.OriginalId);
                    }

                    // 执行更新或插入
                    DataCenter.Database.UpsertDocument(SelectedCollection, finalDoc);
                    
                    // 同步文档状态为“未修改”
                    doc.AcceptChanges();
                    doc.SyncOriginalId();
                }
            }

            CheckUnsavedChanges();
            Console.WriteLine("Changes saved successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving changes: {ex.Message}");
            GridErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 撤销所有挂起的更改，还原到数据库原始状态。
    /// </summary>
    [RelayCommand]
    private void CancelChanges()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        // 1. 还原待删除项到 UI 列表
        foreach (var deleted in _pendingDeletions)
        {
            Documents.Add(deleted);
        }
        _pendingDeletions.Clear();

        // 2. 移除所有尚未提交的新增文档
        var toRemove = Documents.Where(d => d.OriginalId == LiteDB.BsonValue.Null).ToList();
        foreach (var doc in toRemove)
        {
            Documents.Remove(doc);
        }

        // 3. 还原已有文档的值修改
        foreach (var doc in Documents)
        {
            doc.ResetChanges();
        }

        CheckUnsavedChanges();
        this.GridErrorMessage = null;
    }

    /// <summary>
    /// 当错误消息改变时，启动定时器以便 3 秒后自动清除提示。
    /// </summary>
    partial void OnGridErrorMessageChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _validationCts?.Cancel();
            _validationCts = new CancellationTokenSource();
            var token = _validationCts.Token;
            
            Task.Run(async () =>
            {
                await Task.Delay(3000, token);
                if (!token.IsCancellationRequested)
                {
                    GridErrorMessage = null;
                }
            }, token);
        }
    }

    #region 递归 Schema 转换映射助手

    /// <summary>
    /// 将 ClassDefinition 树递归转换为后端 UI 渲染所需的 SchemaProperty 列表。
    /// </summary>
    private List<SchemaProperty> MapClassToProperties(ClassDefinition targetClass, ClassDefinition root)
    {
        var result = new List<SchemaProperty>();
        
        // 仅在解析顶层类（即集合主类）时显式补充 _id 字段定义
        if (targetClass == root)
        {
            result.Add(new SchemaProperty 
            { 
                Name = "_id", 
                DisplayName = "_id", 
                TypeName = "String", 
                IsRequired = true 
            });
        }

        foreach (var field in targetClass.Fields)
        {
            result.Add(MapFieldToProperty(field, root));
        }
        return result;
    }

    /// <summary>
    /// 将单个字段定义映射为 SchemaProperty。
    /// </summary>
    private SchemaProperty MapFieldToProperty(FieldDefinition field, ClassDefinition root)
    {
        var p = new SchemaProperty
        {
            Name = field.FieldName,
            DisplayName = field.FieldName,
            TypeName = MapFieldTypeToString(field.Type),
            IsRequired = true 
        };

        // 处理集合（列表或字典）的内部元素类型映射
        if (field.Type == FieldType.List || field.Type == FieldType.Dictionary)
        {
            p.ElementSchema = new SchemaProperty
            {
                Name = field.Type == FieldType.List ? "Item" : "Value",
                TypeName = MapFieldTypeToString(field.SubType)
            };

            // 如果容器内部是用户定义的类，则递归解析其结构
            if (field.SubType == FieldType.Custom && !string.IsNullOrEmpty(field.SubCustomTypeName))
            {
                p.ElementSchema.CSharpTypeName = field.SubCustomTypeName;
                var subClass = FindClassByName(field.SubCustomTypeName, root);
                if (subClass != null)
                {
                    p.ElementSchema.NestedProperties = MapClassToProperties(subClass, root);
                }
            }
        }
        // 处理直接嵌套的自定义文档类
        else if (field.Type == FieldType.Custom && !string.IsNullOrEmpty(field.CustomTypeName))
        {
            p.CSharpTypeName = field.CustomTypeName;
            var targetClass = FindClassByName(field.CustomTypeName, root);
            if (targetClass != null)
            {
                p.NestedProperties = MapClassToProperties(targetClass, root);
            }
        }

        return p;
    }

    /// <summary>
    /// 将内部 FieldType 枚举映射为 Schema 支持的字符串类型名。
    /// </summary>
    private string MapFieldTypeToString(FieldType type)
    {
        return type switch
        {
            FieldType.Bool => "Boolean",
            FieldType.List => "Array",
            FieldType.Custom => "Document",
            FieldType.Int => "Int32",
            FieldType.Float => "Double",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// 在类定义树中查找指定名称的类。
    /// </summary>
    private ClassDefinition? FindClassByName(string name, ClassDefinition root)
    {
        if (root.ClassName == name) return root;
        return root.InnerClasses.FirstOrDefault(c => c.ClassName == name);
    }

    #endregion
}
