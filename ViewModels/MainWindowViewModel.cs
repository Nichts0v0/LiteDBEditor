using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.Services;

namespace LiteDBEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DatabaseService _dbService;
    private readonly SchemaBindingService _bindingService;

    [ObservableProperty]
    private string _windowTitle = "LiteDB Editor";

    [ObservableProperty]
    private bool _isDatabaseLoaded;

    [ObservableProperty]
    private string? _currentDbFileName;

    [ObservableProperty]
    private ObservableCollection<string> _collections = new();

    [ObservableProperty]
    private string? _selectedCollection;

    [ObservableProperty]
    private ObservableCollection<BsonDocumentWrapper> _documents = new();

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private SchemaData? _currentSchema;

    [ObservableProperty]
    private string? _currentBoundCsFileName;

    [ObservableProperty]
    private string? _currentBoundCsFilePath;

    [ObservableProperty]
    private string? _gridErrorMessage;

    private CancellationTokenSource? _errorCts;

    private readonly ObservableCollection<BsonDocumentWrapper> _pendingDeletions = new();

    public MainWindowViewModel()
    {
        _dbService = new DatabaseService();
        _bindingService = new SchemaBindingService();

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
            _dbService.OpenDatabase(filePath);
            IsDatabaseLoaded = true;
            CurrentDbFileName = System.IO.Path.GetFileName(filePath);
            WindowTitle = $"LiteDB Editor - {_dbService.CurrentDbPath}";

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
        foreach (var colNames in _dbService.GetCollectionNames())
        {
            Collections.Add(colNames);
        }
    }

    // Triggered when the user selects a different collection in the ListBox
    public event EventHandler<SchemaData>? SchemaLoaded;

    partial void OnSelectedCollectionChanged(string? value)
    {
        ClearGridError();
        if (string.IsNullOrEmpty(value)) return;

        LoadCollectionData(value);
    }

    private void LoadCollectionData(string collectionName)
    {
        try
        {
            // 在重置新表前，先清空文档列表
            Documents.Clear();

            var rawDocs = _dbService.GetDocuments(collectionName, 0, 100);

            var parser = new SchemaParserService();
            SchemaData? properties = null;

            if (_dbService.CurrentDbPath != null)
            {
                var boundPath = _bindingService.GetBoundSchemaFilePath(_dbService.CurrentDbPath, collectionName);
                if (!string.IsNullOrEmpty(boundPath))
                {
                    CurrentBoundCsFilePath = boundPath;
                    CurrentBoundCsFileName = System.IO.Path.GetFileName(boundPath);
                    var boundCode = _bindingService.GetBoundSchemaCode(_dbService.CurrentDbPath, collectionName);
                    if (!string.IsNullOrEmpty(boundCode))
                    {
                        properties = parser.ParseFromCSharpSyntax(boundCode, collectionName);
                    }
                }
                else
                {
                    CurrentBoundCsFilePath = null;
                    CurrentBoundCsFileName = null;
                }
            }

            if (properties == null)
            {
                properties = parser.ParseFromBsonDocument(collectionName, rawDocs.FirstOrDefault() ?? new BsonDocument());
            }

            CurrentSchema = properties;

            // 重要：先通知 View 更新列结构，此时 Documents 还是空的，避开渲染冲突
            Console.WriteLine($"[Info] Loading schema for {collectionName}, properties count: {properties.Properties.Count}");
            SchemaLoaded?.Invoke(this, properties);

            // 然后再填充数据，此时 DataGrid 已经准备好了正确的列
            foreach (var doc in rawDocs)
            {
                var wrapper = new BsonDocumentWrapper(doc, OnDocumentModified);
                Documents.Add(wrapper);
            }
            Console.WriteLine($"[Success] Loaded {Documents.Count} documents for {collectionName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] LoadCollectionData failed for {collectionName}: {ex.Message}");
            CurrentSchema = new SchemaData { TargetName = collectionName };
            SchemaLoaded?.Invoke(this, CurrentSchema);
        }
    }

    // 给外部弹窗调用：弹窗编辑后强制触发
    public void ForceSaveDocument(BsonDocumentWrapper wrapper)
    {
        OnDocumentModified(wrapper);
    }

    public void BindSchemaFile(string csFilePath)
    {
        if (string.IsNullOrEmpty(SelectedCollection) || string.IsNullOrEmpty(_dbService.CurrentDbPath)) return;

        _bindingService.BindSchema(_dbService.CurrentDbPath, SelectedCollection, csFilePath);

        LoadCollectionData(SelectedCollection);
    }

    public void CreateCollection(string collectionName, string? boundCsFilePath)
    {
        if (!IsDatabaseLoaded) return;

        _dbService.CreateCollection(collectionName);

        if (!string.IsNullOrEmpty(boundCsFilePath) && _dbService.CurrentDbPath != null)
        {
            _bindingService.BindSchema(_dbService.CurrentDbPath, collectionName, boundCsFilePath);
        }

        RefreshCollections();
        SelectedCollection = collectionName;
    }

    public void DeleteCurrentCollection()
    {
        if (!IsDatabaseLoaded || string.IsNullOrEmpty(SelectedCollection)) return;

        _dbService.DropCollection(SelectedCollection);
        RefreshCollections();
        SelectedCollection = Collections.FirstOrDefault();
    }

    [RelayCommand]
    private void DeleteSpecificCollection(string collectionName)
    {
        if (!IsDatabaseLoaded || string.IsNullOrEmpty(collectionName)) return;

        _dbService.DropCollection(collectionName);
        RefreshCollections();
        if (SelectedCollection == collectionName)
        {
            SelectedCollection = Collections.FirstOrDefault();
        }
    }

    /// <summary>
    /// 将文档标记为待删除（软删除），仅从界面移除并存入待处理队列。
    /// </summary>
    public void MarkDocumentForDeletion(BsonDocumentWrapper wrapper)
    {
        if (Documents.Remove(wrapper))
        {
            _pendingDeletions.Add(wrapper);
            CheckUnsavedChanges();
        }
    }

    private void OnDocumentModified(BsonDocumentWrapper wrapper)
    {
        // 现在不立即存盘，只统计是否有脏数据交由 UI 判断
        CheckUnsavedChanges();
    }

    private void CheckUnsavedChanges()
    {
        HasUnsavedChanges = Documents.Any(d => d.IsModified) || _pendingDeletions.Any();
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
                if (deleted.OriginalId != BsonValue.Null)
                {
                    _dbService.DeleteDocument(SelectedCollection, deleted.OriginalId);
                }
            }
            _pendingDeletions.Clear();

            // 2. 处理已修改或新增的数据
            foreach (var doc in Documents)
            {
                if (doc.IsModified)
                {
                    // 检查 ID 是否发生变更
                    var currentId = doc.GetRawValue("_id");
                    if (doc.OriginalId != BsonValue.Null && doc.OriginalId != currentId)
                    {
                        // ID 变了，先删除旧键记录，防止产生重复项
                        _dbService.DeleteDocument(SelectedCollection, doc.OriginalId);
                    }

                    doc.AcceptChanges();
                    _dbService.UpsertDocument(SelectedCollection, doc.Document);
                    doc.SyncOriginalId(); // 更新原始 ID 追踪，使其在下次修改时以此为准
                }
            }
            CheckUnsavedChanges();
            Console.WriteLine("[Success] Changes saved to database.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] SaveChanges failed: {ex.Message}");
            // TODO: 这里可以考虑通知 UI 弹窗报错，但至少现在不会导致程序直接闪退
        }
    }

    [RelayCommand]
    private void CancelChanges()
    {
        // 1. 还原已修改的数据
        foreach (var doc in Documents)
        {
            if (doc.IsModified)
            {
                doc.RejectChanges();
            }
        }

        // 2. 还原被删除的数据（插回界面显示）
        foreach (var deleted in _pendingDeletions)
        {
            Documents.Add(deleted);
        }
        _pendingDeletions.Clear();

        CheckUnsavedChanges();
    }

    /// <summary>
    /// 检测给定的 ID 是否与当前显示列表中的任何行冲突。
    /// 校验范围包含原始数据和尚未保存的更改。
    /// </summary>
    public bool IsIdDuplicate(BsonValue newId, BsonDocumentWrapper? excludeWrapper = null)
    {
        return Documents.Any(d =>
            d != excludeWrapper &&
            d.GetRawValue("_id") == newId);
    }

    public async void ClearGridError()
    {
        _errorCts?.Cancel();
        _errorCts = null;
        GridErrorMessage = null;
    }

    partial void OnGridErrorMessageChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // 每次设置新错误时，重置自动清除计时器
            _errorCts?.Cancel();
            _errorCts = new CancellationTokenSource();
            var token = _errorCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000, token);
                    if (!token.IsCancellationRequested)
                    {
                        GridErrorMessage = null;
                    }
                }
                catch (TaskCanceledException) { /* 忽略取消 */ }
            }, token);
        }
    }
}
