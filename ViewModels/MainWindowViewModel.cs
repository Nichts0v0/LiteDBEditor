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

    [ObservableProperty]
    private ObservableCollection<string> _collections = new();

    [ObservableProperty]
    private string? _selectedCollection;

    [ObservableProperty]
    private string? _currentBoundCsFilePath;

    [ObservableProperty]
    private string? _currentBoundCsFileName;

    [ObservableProperty]
    private ObservableCollection<BsonDocumentWrapper> _documents = new();

    [ObservableProperty]
    private SchemaData? _currentSchema;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string? _gridErrorMessage;

    private readonly List<BsonDocumentWrapper> _pendingDeletions = new();
    private CancellationTokenSource? _validationCts;

    public event EventHandler<SchemaData?>? SchemaLoaded;

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

    public Dictionary<string, string> AvailableLanguages => LanguageService.AvailableLanguages;

    [RelayCommand]
    private void ChangeLanguage(string locale)
    {
        if (locale != LanguageService.CurrentLanguage)
        {
            LanguageService.SetLanguage(locale);
            OnPropertyChanged(nameof(SelectedLanguage));
        }
    }

    public string SelectedLanguage => LanguageService.CurrentLanguage;

    public MainWindowViewModel()
    {
        // 监听集合变更，确保增删文档时也能触发保存按钮的状态同步
        Documents.CollectionChanged += (s, e) => CheckUnsavedChanges();
    }

    /// <summary>
    /// Open a database file (Invoked from UI with file path)
    /// </summary>
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
            // TODO: Better error handling/dialog
            Console.WriteLine($"Error opening database: {ex.Message}");
        }
    }

    private void RefreshCollections()
    {
        if (!IsDatabaseLoaded) return;

        Collections.Clear();
        foreach (var colNames in DataCenter.Database.GetCollectionNames())
        {
            Collections.Add(colNames);
        }
    }

    private void LoadCollectionData(string collectionName)
    {
        try
        {
            // 在重置新表前，先清空文档列表
            Documents.Clear();

            var rawDocs = DataCenter.Database.GetDocuments(collectionName, 0, 100);

            var parser = new SchemaParserService();
            SchemaData? properties = null;

            if (DataCenter.Database.CurrentDbPath != null)
            {
                var boundPath = DataCenter.Bindings.GetBoundSchemaFilePath(DataCenter.Database.CurrentDbPath, collectionName);
                if (!string.IsNullOrEmpty(boundPath))
                {
                    CurrentBoundCsFilePath = boundPath;
                    CurrentBoundCsFileName = System.IO.Path.GetFileName(boundPath);
                    
                    // 核心变更：从 .schema.json 加载元数据，并执行递归深层转换
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

            // 如果没有绑定成功，尝试根据第一个文档自动推断（保持旧逻辑兼容性）
            if (properties == null && rawDocs.Count > 0)
            {
                properties = parser.ParseFromBsonDocument(collectionName, rawDocs.FirstOrDefault() ?? new BsonDocument());
            }

            CurrentSchema = properties;
            SchemaLoaded?.Invoke(this, properties);

            foreach (var doc in rawDocs)
            {
                var docViewModel = new BsonDocumentWrapper(doc, d => CheckUnsavedChanges());
                // 监听文档中的值变更
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

    public void BindSchemaFile(string csFilePath)
    {
        if (string.IsNullOrEmpty(SelectedCollection) || string.IsNullOrEmpty(DataCenter.Database.CurrentDbPath)) return;

        DataCenter.Bindings.BindSchema(DataCenter.Database.CurrentDbPath, SelectedCollection, csFilePath);

        LoadCollectionData(SelectedCollection);
    }

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

    public void DeleteCurrentCollection()
    {
        if (!IsDatabaseLoaded || string.IsNullOrEmpty(SelectedCollection)) return;

        DataCenter.Database.DropCollection(SelectedCollection);
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault();
    }

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
    /// 清除网格级别的全局错误提示
    /// </summary>
    public void ClearGridError()
    {
        GridErrorMessage = null;
    }

    /// <summary>
    /// 检查指定 ID 是否在当前集合或内存文档中存在重复
    /// </summary>
    public bool IsIdDuplicate(BsonValue newVal, BsonDocumentWrapper? exclude = null)
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return false;

        // 1. 检查当前 ObservableCollection 中的内存数据
        foreach (var doc in Documents)
        {
            if (doc == exclude) continue;
            var currentId = doc.GetRawValue("_id");
            if (currentId == newVal) return true;
        }

        // 2. 只有当 exclude 不为 null 时（即在编辑现有文档），才需要检查数据库中是否存在由于未完全加载导致的 ID 冲突
        // (如果是新建文档，上面循环已经涵盖了所有尚未提交的新 ID)
        return false; 
    }

    /// <summary>
    /// 标记文档待删除，并从 UI 列表中移除
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
    /// 强制执行一次单个文档的保存 (通常用于弹窗保存后的立即同步)
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

    public void RenameCollection(string oldName, string newName)
    {
        if (!IsDatabaseLoaded || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || oldName == newName) return;

        try
        {
            bool success = false;
            // 针对 LiteDB 的处理：如果只是大小写不同，直接重命名可能会失败
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
                // 如果当前正在查看这张表，则更新选中项名称，触发重新加载（主要是更新标题等 UI）
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

    [RelayCommand]
    private void AddNewDocument()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        var newDoc = new BsonDocument();
        // 如果有 Schema，尝试初始化一些字段
        if (CurrentSchema != null)
        {
            foreach (var prop in CurrentSchema.Properties)
            {
                if (prop.Name == "_id") continue;
                newDoc[prop.Name] = BsonValue.Null;
            }
        }

        var wrapper = new BsonDocumentWrapper(newDoc, d => CheckUnsavedChanges());
        // wrapper.IsNew = true; // BsonDocumentWrapper 暂时没有 IsNew，通过 OriginalId == Null 来判定
        wrapper.PropertyChanged += (s, e) => CheckUnsavedChanges();
        
        Documents.Add(wrapper);
        CheckUnsavedChanges();
    }

    [RelayCommand]
    private void DeleteDocument(BsonDocumentWrapper doc)
    {
        if (doc == null) return;
        
        // 记录待删除，点击保存时才执行物理删除
        _pendingDeletions.Add(doc);
        Documents.Remove(doc);
        CheckUnsavedChanges();
    }

    private void CheckUnsavedChanges()
    {
        HasUnsavedChanges = _pendingDeletions.Any() || Documents.Any(d => d.IsModified || d.OriginalId == LiteDB.BsonValue.Null);
    }

    [RelayCommand]
    private void SaveChanges()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        try
        {
            // 1. 处理待删除的数据
            foreach (var deleted in _pendingDeletions)
            {
                // 如果 OriginalId 不为空，说明原来是数据库里的数据，执行物理删除
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

                    // 检查 ID 是否发生变更
                    var currentId = finalDoc["_id"];
                    if (doc.OriginalId != LiteDB.BsonValue.Null && doc.OriginalId != currentId)
                    {
                        // ID 变了，先删除旧键记录
                        DataCenter.Database.DeleteDocument(SelectedCollection, doc.OriginalId);
                    }

                    // 核心修复：直接使用过滤、合并后的 finalDoc 进行保存
                    DataCenter.Database.UpsertDocument(SelectedCollection, finalDoc);
                    
                    // 同步状态
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

    [RelayCommand]
    private void CancelChanges()
    {
        if (string.IsNullOrEmpty(SelectedCollection)) return;

        // 1. 还原待删除项
        foreach (var deleted in _pendingDeletions)
        {
            Documents.Add(deleted);
        }
        _pendingDeletions.Clear();

        // 2. 还原所有文档的修改，并移除尚未入库的新增文档
        var toRemove = Documents.Where(d => d.OriginalId == LiteDB.BsonValue.Null).ToList();
        foreach (var doc in toRemove)
        {
            Documents.Remove(doc);
        }

        foreach (var doc in Documents)
        {
            doc.ResetChanges();
        }

        CheckUnsavedChanges();
        this.GridErrorMessage = null;
    }

    partial void OnGridErrorMessageChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // 3秒后自动清除错误消息
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
    /// 将 ClassDefinition 转换为后端 UI 渲染所需的 SchemaProperty 列表
    /// </summary>
    private List<SchemaProperty> MapClassToProperties(ClassDefinition targetClass, ClassDefinition root)
    {
        var result = new List<SchemaProperty>();
        
        // 显式添加 ID 字段（LiteDB 约定）
        result.Add(new SchemaProperty 
        { 
            Name = "_id", 
            DisplayName = "_id", 
            TypeName = "String", // 默认显示为字符串
            IsRequired = true 
        });

        foreach (var field in targetClass.Fields)
        {
            result.Add(MapFieldToProperty(field, root));
        }
        return result;
    }

    private SchemaProperty MapFieldToProperty(FieldDefinition field, ClassDefinition root)
    {
        var p = new SchemaProperty
        {
            Name = field.FieldName,
            DisplayName = field.FieldName,
            TypeName = MapFieldTypeToString(field.Type),
            IsRequired = true // 默认必填
        };

        // 处理复杂容器或嵌套
        if (field.Type == FieldType.List || field.Type == FieldType.Dictionary)
        {
            p.ElementSchema = new SchemaProperty
            {
                Name = field.Type == FieldType.List ? "Item" : "Value",
                TypeName = MapFieldTypeToString(field.SubType)
            };

            // 如果容器内部是自定义类
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

    private ClassDefinition? FindClassByName(string name, ClassDefinition root)
    {
        if (root.ClassName == name) return root;
        return root.InnerClasses.FirstOrDefault(c => c.ClassName == name);
    }

    #endregion
}
