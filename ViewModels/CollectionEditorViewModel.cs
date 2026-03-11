using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 代表集合编辑器中的单行条目（Array 的一个元素或 Dict 的 key-value 对）。
/// </summary>
public partial class CollectionItemRow : ObservableObject
{
    #region 展示属性

    /// <summary>Array 模式下显示索引，Dict 模式下显示 key</summary>
    [ObservableProperty]
    private string _keyLabel = string.Empty;

    /// <summary>当前值的文本预览（基础类型直接展示，复杂类型展示占位描述）</summary>
    [ObservableProperty]
    private string _valuePreview = string.Empty;

    #endregion

    #region 内部引用（供编辑命令和父 ViewModel 使用）

    /// <summary>若为 Array 模式：在父 BsonArray 中的真实 BsonValue 引用。</summary>
    public BsonValue? BsonVal { get; set; }

    /// <summary>若为 Dict 模式：该条目在父 BsonDocument 中的 key。</summary>
    public string? DictKey { get; set; }

    #endregion
}

/// <summary>
/// 通用集合编辑弹窗的 ViewModel，支持三种模式：Array / Dictionary / Document（子嵌套）。
/// </summary>
public partial class CollectionEditorViewModel : ViewModelBase
{
    #region 弹窗元数据与集合

    [ObservableProperty]
    private string _title = "编辑集合";

    [ObservableProperty]
    private string? _contextPath;

    [ObservableProperty]
    private ObservableCollection<CollectionItemRow> _items = new();

    /// <summary>
    /// 记录本次打开弹窗期间数据是否发生了变动（增删改）。
    /// </summary>
    public bool IsChanged { get; private set; } = false;

    /// <summary>
    /// 标记数据已变动。
    /// </summary>
    public void NotifyChanged() => IsChanged = true;

    /// <summary>
    /// 当前编辑模式字符串，用于判断 UI 行为。
    /// 值为 "Array" / "Dictionary" / "Document"
    /// </summary>
    public string EditMode { get; private set; } = "Array";

    // 内部持有的底层 Bson 数据结构
    private BsonArray? _backingArray;
    private BsonDocument? _backingDocument;

    // 元素/value 的 Schema 描述，供递归弹窗使用
    public SchemaProperty? ElementSchema { get; private set; }

    // 用于在添加 Document 类型 value 时可以递归打开子弹窗的回调
    // 由 View 层（CollectionEditorWindow）负责注入
    public Func<SchemaProperty, BsonDocument, System.Threading.Tasks.Task>? OpenSubEditorAsync { get; set; }

    #endregion

    #region 初始化

    /// <summary>以 Array 模式初始化（BsonArray）</summary>
    public void InitializeAsArray(BsonArray array, SchemaProperty schema, string? contextPath = null)
    {
        EditMode = "Array";
        _backingArray = array;
        ElementSchema = schema.ElementSchema;
        ContextPath = contextPath;
        Title = string.IsNullOrEmpty(ContextPath)
            ? $"编辑列表 · {schema.DisplayName}"
            : $"编辑: {ContextPath}";
        RefreshItems();
    }

    /// <summary>以 Dictionary 模式初始化（BsonDocument，key 为 string）</summary>
    public void InitializeAsDictionary(BsonDocument dict, SchemaProperty schema, string? contextPath = null)
    {
        EditMode = "Dictionary";
        _backingDocument = dict;
        ElementSchema = schema.ElementSchema;  // value 的 Schema
        ContextPath = contextPath;
        Title = string.IsNullOrEmpty(ContextPath)
            ? $"编辑字典 · {schema.DisplayName}"
            : $"编辑: {ContextPath}";
        RefreshItems();
    }

    /// <summary>以 Document 模式初始化（BsonDocument，用于嵌套子类编辑）</summary>
    public void InitializeAsDocument(BsonDocument doc, SchemaProperty schema, string? contextPath = null)
    {
        EditMode = "Document";
        _backingDocument = doc;
        ContextPath = contextPath;
        // 对 Document 模式，NestedProperties 就是所有字段
        Title = string.IsNullOrEmpty(ContextPath)
            ? $"编辑对象 · {schema.DisplayName}"
            : $"编辑: {ContextPath}";
        RefreshItems();
    }

    /// <summary>根据底层数据重建 Items 列表</summary>
    public void RefreshItems()
    {
        Items.Clear();

        if (EditMode == "Array" && _backingArray != null)
        {
            for (int i = 0; i < _backingArray.Count; i++)
            {
                var val = _backingArray[i];
                Items.Add(new CollectionItemRow
                {
                    KeyLabel = $"[{i}]",
                    ValuePreview = GetBsonPreview(val),
                    BsonVal = val
                });
            }
        }
        else if (EditMode == "Dictionary" && _backingDocument != null)
        {
            foreach (var kvp in _backingDocument)
            {
                Items.Add(new CollectionItemRow
                {
                    KeyLabel = kvp.Key,
                    ValuePreview = GetBsonPreview(kvp.Value),
                    BsonVal = kvp.Value,
                    DictKey = kvp.Key
                });
            }
        }
        else if (EditMode == "Document" && _backingDocument != null)
        {
            foreach (var kvp in _backingDocument)
            {
                Items.Add(new CollectionItemRow
                {
                    KeyLabel = kvp.Key,
                    ValuePreview = GetBsonPreview(kvp.Value),
                    BsonVal = kvp.Value,
                    DictKey = kvp.Key
                });
            }
        }
    }

    private static string GetBsonPreview(BsonValue val)
    {
        if (val.IsDocument) return $"[对象 {val.AsDocument.Count} 个字段]";
        if (val.IsArray) return $"[列表 {val.AsArray.Count} 项]";
        if (val.IsNull) return "(null)";
        return val.RawValue?.ToString() ?? "";
    }

    #endregion

    #region 命令 — Array 模式

    /// <summary>向 Array 中追加一个基础类型（string/int/bool）条目</summary>
    [RelayCommand]
    private void AddPrimitiveItem(string? rawValue)
    {
        if (_backingArray == null) return;

        var typeName = ElementSchema?.TypeName ?? "String";
        var bval = ConvertToBson(rawValue ?? "", typeName);
        _backingArray.Add(bval);
        NotifyChanged();
        RefreshItems();
    }

    /// <summary>删除 Array 中指定行</summary>
    [RelayCommand]
    private void RemoveArrayItem(CollectionItemRow? row)
    {
        if (_backingArray == null || row == null) return;

        // 通过 ValuePreview + KeyLabel 定位原始索引
        if (_backingArray != null && int.TryParse(row.KeyLabel.Trim('[', ']'), out var idx) && idx < _backingArray.Count)
        {
            _backingArray.RemoveAt(idx);
            NotifyChanged();
        }
        RefreshItems();
    }

    #endregion

    #region 命令 — Dictionary 模式

    /// <summary>向 Dictionary 中追加一条基础类型 value 记录（key 由调用方传入）</summary>
    public void AddDictItemPrimitive(string key, string rawValue)
    {
        if (_backingDocument == null || string.IsNullOrWhiteSpace(key)) return;

        var typeName = ElementSchema?.TypeName ?? "String";
        var bval = ConvertToBson(rawValue, typeName);
        _backingDocument[key] = bval;
        NotifyChanged();
        RefreshItems();
    }

    /// <summary>向 Dictionary 中追加一条 Document（嵌套对象）value 记录</summary>
    public void AddDictItemDocument(string key, BsonDocument value)
    {
        if (_backingDocument == null || string.IsNullOrWhiteSpace(key)) return;

        _backingDocument[key] = value;
        NotifyChanged();
        RefreshItems();
    }

    [RelayCommand]
    private void RemoveDictItem(CollectionItemRow? row)
    {
        if (_backingDocument == null || row?.DictKey == null || EditMode == "Document") return;

        _backingDocument.Remove(row.DictKey);
        NotifyChanged();
        RefreshItems();
    }

    #endregion

    #region 辅助方法

    private static BsonValue ConvertToBson(string raw, string typeName)
    {
        // 数值类型收到空字符串时，默认使用 0
        if (string.IsNullOrWhiteSpace(raw) && typeName is "Int32" or "Int64" or "Double")
            raw = "0";

        try
        {
            return typeName switch
            {
                "Boolean" => new BsonValue(bool.Parse(raw)),
                "Double" => new BsonValue(double.Parse(raw)),
                "Int32" => new BsonValue(int.Parse(raw)),
                "Int64" => new BsonValue(long.Parse(raw)),
                _ => new BsonValue(raw)
            };
        }
        catch
        {
            return new BsonValue(raw);
        }
    }

    /// <summary>返回底层的 BsonArray（Array 模式）</summary>
    public BsonArray? GetBackingArray() => _backingArray;

    /// <summary>返回底层的 BsonDocument（Dictionary / Document 模式）</summary>
    public BsonDocument? GetBackingDocument() => _backingDocument;

    #endregion
}
