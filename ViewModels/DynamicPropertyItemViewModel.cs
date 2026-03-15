using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.ViewModels;

public partial class DynamicPropertyItemViewModel : ViewModelBase
{
    #region 属性基础信息与状态

    [ObservableProperty]
    private string _propertyName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeNameDisplay))]
    private string _typeName = "String";

    public string TypeNameDisplay => PropertySchema?.GetFriendlyTypeString() ?? TypeName;

    [ObservableProperty]
    private bool _isRequired;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 完整的 SchemaProperty 元数据，供打开子编辑器时使用（包含 ElementSchema 和 NestedProperties）。
    /// </summary>
    public SchemaProperty? PropertySchema { get; private set; }

    /// <summary>
    /// 查重回调支持：(key, bsonValue) => 是否冲突
    /// </summary>
    public Func<string, BsonValue, bool>? IdDuplicateCheckFunc { get; set; }

    private string? _originalKey;
    private object? _originalValue;

    #endregion

    #region 复杂类型支持 (递归)

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<DynamicPropertyItemViewModel> _children = new();

    /// <summary>标记当前项是否为 Dictionary 的条目，以便控制 Key 输入框可见性</summary>
    [ObservableProperty]
    private bool _isDictionaryItem;

    /// <summary>Dictionary 模式下的 Key 值输入（双向绑定）</summary>
    [ObservableProperty]
    private string? _dictItemKey;

    #endregion

    #region 属性值管理

    // 存储底层 BsonDocument 或者 BsonArray 和它对应的 Key 或者 Index
    private BsonDocument? _parentDocument;
    private BsonArray? _parentArray;
    private string? _documentKey;
    private int _arrayIndex = -1;

    // 回调用于删除自身
    private Action<DynamicPropertyItemViewModel>? _onRemoveRequested;
    public bool CanRemove => _onRemoveRequested != null;
    public bool CanAddChildren => TypeName == "Array" || TypeName == "Dictionary";

    /// <summary>
    /// 内存当前字段在父 BsonDocument 中的原始 BsonValue，
    /// 供开启子编辑器时直接使用（数组/字典/嵌套类）。
    /// </summary>
    public BsonValue? CurrentBsonValue { get; private set; }

    // 通用的输入值绑定
    [ObservableProperty]
    private object? _value;

    [ObservableProperty]
    private bool _isStringEditorVisible;

    /// <summary>Int32 / Int64 / Double 类型共用同一个文本框，可以在 Loaded 事件中附加输入过滤</summary>
    [ObservableProperty]
    private bool _isNumericEditorVisible;

    [ObservableProperty]
    private bool _isBooleanEditorVisible;

    [ObservableProperty]
    private bool _isComplexEditorVisible;

    [ObservableProperty]
    private string _complexTypePreview = string.Empty;

    public void InitializeWithDocument(BsonDocument parent, string key, SchemaProperty schema, Action<DynamicPropertyItemViewModel>? onRemove = null)
    {
        _parentDocument = parent;
        _documentKey = key;
        _originalKey = key;
        PropertySchema = schema;
        _onRemoveRequested = onRemove;

        PropertyName = schema.Name;
        DisplayName = schema.DisplayName; 
        TypeName = schema.TypeName;
        IsRequired = schema.IsRequired;

        var bsonVal = parent.TryGetValue(key, out var val) ? val : BsonValue.Null;
        _originalValue = bsonVal.RawValue;
        UpdateVisibilityAndValue(schema, bsonVal);
    }

    public void InitializeWithArray(BsonArray parent, int index, SchemaProperty schema, Action<DynamicPropertyItemViewModel>? onRemove = null)
    {
        _parentArray = parent;
        _arrayIndex = index;
        PropertySchema = schema;
        _onRemoveRequested = onRemove;

        PropertyName = $"[{index}]";
        DisplayName = $"Item {index}";
        TypeName = schema.TypeName;

        var bsonVal = index < parent.Count ? parent[index] : BsonValue.Null;
        _originalValue = bsonVal.RawValue;
        UpdateVisibilityAndValue(schema, bsonVal);
    }

    /// <summary>用于初始化 Dictionary 中的具体键值对</summary>
    public void InitializeAsDictionaryItem(BsonDocument parent, string key, SchemaProperty valueSchema, Action<DynamicPropertyItemViewModel>? onRemove = null)
    {
        _parentDocument = parent;
        _documentKey = key;
        DictItemKey = key;
        PropertySchema = valueSchema;
        _onRemoveRequested = onRemove;

        IsDictionaryItem = true;
        PropertyName = key;
        DisplayName = string.Empty; // Dictionary 项主要显示 Key 输入框
        TypeName = valueSchema.TypeName;

        var bsonVal = parent.TryGetValue(key, out var val) ? val : BsonValue.Null;
        _originalValue = bsonVal.RawValue;
        UpdateVisibilityAndValue(valueSchema, bsonVal);
    }

    partial void OnValueChanged(object? value)
    {
        ErrorMessage = null;
        
        // 实时校验：针对 _id 字段
        if (PropertyName == "_id")
        {
            var strVal = value?.ToString();
            if (string.IsNullOrWhiteSpace(strVal))
            {
                ErrorMessage = "ID 不能为空";
            }
            else if (IdDuplicateCheckFunc != null)
            {
                var bsonVal = ConvertToBsonValue(value, TypeName);
                if (IdDuplicateCheckFunc.Invoke("_id", bsonVal))
                {
                    ErrorMessage = $"ID '{strVal}' 重复";
                }
            }
        }

        WriteValueToBackingStore();
    }

    partial void OnDictItemKeyChanged(string? value)
    {
        if (!IsDictionaryItem || _parentDocument == null || _documentKey == null) return;

        // 重要：如果新值等于当前正式生效的 Key (可能是由于验证失败触发的回退)，则不清除错误信息也不执行后续逻辑
        if (value == _documentKey) return;

        ErrorMessage = null;
        
        // 1. 如果新值为空，直接报错并回退
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = "键名不能为空";
            // 强制将 UI 绑定的值回退为底层存储的旧 Key
            DictItemKey = _documentKey; 
            return;
        }

        // 2. 检查冲突
        var collision = _parentDocument.Keys.Contains(value);
        if (collision)
        {
            ErrorMessage = $"键名 '{value}' 已存在";
            // 强制将 UI 绑定的值回退
            DictItemKey = _documentKey;
            return;
        }

        // 3. 校验通过，执行物理移动
        var currentVal = _parentDocument[_documentKey];
        _parentDocument.Remove(_documentKey);
        _parentDocument[value] = currentVal;
        _documentKey = value;
        PropertyName = value;
    }

    private void WriteValueToBackingStore()
    {
        if (_parentDocument != null && _documentKey != null)
        {
            _parentDocument[_documentKey] = ConvertToBsonValue(Value, TypeName);
        }
        else if (_parentArray != null && _arrayIndex >= 0)
        {
            if (_arrayIndex < _parentArray.Count)
                _parentArray[_arrayIndex] = ConvertToBsonValue(Value, TypeName);
        }
    }

    private void UpdateVisibilityAndValue(SchemaProperty schema, BsonValue bsonVal)
    {
        CurrentBsonValue = bsonVal;
        IsStringEditorVisible = false;
        IsNumericEditorVisible = false;
        IsBooleanEditorVisible = false;
        IsComplexEditorVisible = false;
        Children.Clear();

        switch (schema.TypeName)
        {
            case "String":
                Value = bsonVal.IsString ? bsonVal.AsString : bsonVal.RawValue?.ToString() ?? "";
                IsStringEditorVisible = true;
                break;

            case "Int32":
            case "Int64":
                Value = bsonVal.IsNumber ? bsonVal.RawValue?.ToString() : "0";
                IsNumericEditorVisible = true;
                break;

            case "Double":
                Value = bsonVal.IsNumber ? bsonVal.AsDouble.ToString() : "0.0";
                IsNumericEditorVisible = true;
                break;

            case "Boolean":
                Value = bsonVal.IsBoolean ? bsonVal.AsBoolean : false;
                IsBooleanEditorVisible = true;
                break;

            case "Dictionary":
                IsComplexEditorVisible = true;
                var dict = bsonVal.IsDocument ? bsonVal.AsDocument : new BsonDocument();
                if (!bsonVal.IsDocument)
                {
                    if (_parentArray != null) _parentArray[_arrayIndex] = dict;
                    if (_parentDocument != null && _documentKey != null) _parentDocument[_documentKey] = dict;
                    CurrentBsonValue = dict;
                }

                foreach (var kvp in dict)
                {
                    var child = new DynamicPropertyItemViewModel();
                    child.InitializeAsDictionaryItem(dict, kvp.Key, schema.ElementSchema ?? new SchemaProperty { TypeName = "String" },
                        item => RemoveChildInternal(item));
                    Children.Add(child);
                }
                ComplexTypePreview = $"[{Children.Count} 项键值对]";
                break;

            case "Array":
                IsComplexEditorVisible = true;
                var arr = bsonVal.IsArray ? bsonVal.AsArray : new BsonArray();
                if (!bsonVal.IsArray)
                {
                    if (_parentArray != null) _parentArray[_arrayIndex] = arr;
                    if (_parentDocument != null && _documentKey != null) _parentDocument[_documentKey] = arr;
                    CurrentBsonValue = arr;
                }

                for (int i = 0; i < arr.Count; i++)
                {
                    var child = new DynamicPropertyItemViewModel();
                    child.InitializeWithArray(arr, i, schema.ElementSchema ?? new SchemaProperty { TypeName = "String" },
                        item => RemoveChildInternal(item));
                    Children.Add(child);
                }
                ComplexTypePreview = $"[{Children.Count} 项]";
                break;

            case "Document":
                IsComplexEditorVisible = true;
                var doc = bsonVal.IsDocument ? bsonVal.AsDocument : new BsonDocument();
                if (!bsonVal.IsDocument)
                {
                    if (_parentArray != null) _parentArray[_arrayIndex] = doc;
                    if (_parentDocument != null && _documentKey != null) _parentDocument[_documentKey] = doc;
                    CurrentBsonValue = doc;
                }

                if (schema.NestedProperties != null)
                {
                    foreach (var prop in schema.NestedProperties)
                    {
                        var child = new DynamicPropertyItemViewModel();
                        child.InitializeWithDocument(doc, prop.Name, prop);
                        Children.Add(child);
                    }
                }
                ComplexTypePreview = "[嵌套对象]";
                break;

            default:
                IsStringEditorVisible = true;
                Value = bsonVal.RawValue?.ToString() ?? "";
                break;
        }
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void AddChild()
    {
        if (TypeName == "Array" && CurrentBsonValue is BsonArray arr)
        {
            // 校验：如果当前最后一项是空的字符串/数字0（简单判断），则不允许添加
            if (Children.Count > 0)
            {
                var last = Children.Last();
                if (IsItemEmpty(last))
                {
                    ErrorMessage = "请先完善当前最后一项后再添加新项";
                    return;
                }
            }

            var elementSchema = PropertySchema?.ElementSchema ?? new SchemaProperty { TypeName = "String" };
            var newValue = CreateDefaultValue(elementSchema.TypeName);
            arr.Add(newValue);

            var child = new DynamicPropertyItemViewModel();
            child.InitializeWithArray(arr, arr.Count - 1, elementSchema, item => RemoveChildInternal(item));
            Children.Add(child);
            IsExpanded = true;
            RefreshComplexPreview();
        }
        else if (TypeName == "Dictionary" && CurrentBsonValue is BsonDocument dict)
        {
            if (Children.Count > 0)
            {
                var last = Children.Last();
                if (IsItemEmpty(last) || string.IsNullOrWhiteSpace(last.DictItemKey))
                {
                    ErrorMessage = "请先完善当前最后一项的 Key 和内容";
                    return;
                }
            }

            // 生成一个不重复的临时 Key
            string newKey = "new_key";
            int count = 1;
            while (dict.ContainsKey(newKey)) newKey = $"new_key_{count++}";

            var elementSchema = PropertySchema?.ElementSchema ?? new SchemaProperty { TypeName = "String" };
            dict[newKey] = CreateDefaultValue(elementSchema.TypeName);

            var child = new DynamicPropertyItemViewModel();
            child.InitializeAsDictionaryItem(dict, newKey, elementSchema, item => RemoveChildInternal(item));
            Children.Add(child);
            IsExpanded = true;
            RefreshComplexPreview();
        }
    }

    private void RemoveChildInternal(DynamicPropertyItemViewModel child)
    {
        if (TypeName == "Array" && CurrentBsonValue is BsonArray arr)
        {
            int idx = Children.IndexOf(child);
            if (idx >= 0)
            {
                arr.RemoveAt(idx);
                Children.RemoveAt(idx);
                // 重新刷新后续子项的索引和显示
                for (int i = idx; i < Children.Count; i++)
                {
                    var elementSchema = PropertySchema?.ElementSchema ?? new SchemaProperty { TypeName = "String" };
                    Children[i].InitializeWithArray(arr, i, elementSchema, item => RemoveChildInternal(item));
                }
                RefreshComplexPreview();
            }
        }
        else if (TypeName == "Dictionary" && CurrentBsonValue is BsonDocument dict)
        {
            if (child.DictItemKey != null && dict.Remove(child.DictItemKey))
            {
                Children.Remove(child);
                RefreshComplexPreview();
            }
        }
    }

    [RelayCommand]
    private void RemoveSelf() => _onRemoveRequested?.Invoke(this);

    private bool IsItemEmpty(DynamicPropertyItemViewModel item)
    {
        if (item.IsComplexEditorVisible) return false;
        if (item.TypeName == "Int32" || item.TypeName == "Int64" || item.TypeName == "Double") return false;
        var v = item.Value?.ToString();
        return string.IsNullOrEmpty(v);
    }

    private BsonValue CreateDefaultValue(string typeName)
    {
        return typeName switch
        {
            "Int32" => 0,
            "Int64" => 0L,
            "Double" => 0.0,
            "Boolean" => false,
            "Array" => new BsonArray(),
            "Dictionary" or "Document" => new BsonDocument(),
            _ => ""
        };
    }

    public void RefreshComplexPreview()
    {
        if (CurrentBsonValue == null) return;
        if (CurrentBsonValue.IsArray)
            ComplexTypePreview = $"[{Children.Count} 项]";
        else if (CurrentBsonValue.IsDocument)
            ComplexTypePreview = $"[{Children.Count} 项键值对]";
    }

    public bool Validate(out string? error)
    {
        error = null;

        // 优先检查已有的实时错误
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            error = ErrorMessage;
            return false;
        }

        if (IsDictionaryItem)
        {
            if (string.IsNullOrWhiteSpace(DictItemKey))
            {
                ErrorMessage = "Key 不能为空";
                error = ErrorMessage;
                return false;
            }
        }

        foreach (var child in Children)
        {
            if (!child.Validate(out error))
            {
                IsExpanded = true;
                return false;
            }
            
            if (TypeName == "Dictionary")
            {
                var keys = Children.Where(c => c != child && !string.IsNullOrEmpty(c.DictItemKey)).Select(c => c.DictItemKey);
                if (keys.Contains(child.DictItemKey))
                {
                    child.ErrorMessage = $"Key '{child.DictItemKey}' 重复";
                    error = child.ErrorMessage;
                    IsExpanded = true;
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 递归收集当前项及其子项的所有错误字段名称，并返回第一个错误项 VM 供滚动焦点
    /// </summary>
    public DynamicPropertyItemViewModel? CollectAllErrors(System.Collections.Generic.List<string> errorDisplayNames)
    {
        // 触发当前的同步验证（补漏）
        Validate(out _);

        DynamicPropertyItemViewModel? firstErrorVm = null;

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            // 如果是字典项，我们不直接在页脚显示那个随机/重复的 Key 字符串，而是让父级 Dictionary 报告变量名
            if (!IsDictionaryItem)
            {
                string name = string.IsNullOrEmpty(DisplayName) ? PropertyName : DisplayName;
                if (!errorDisplayNames.Contains(name))
                {
                    errorDisplayNames.Add(name);
                }
            }
            firstErrorVm = this;
        }

        foreach (var child in Children)
        {
            var childErrorVm = child.CollectAllErrors(errorDisplayNames);
            if (childErrorVm != null)
            {
                if (firstErrorVm == null) firstErrorVm = childErrorVm;

                // 如果当前项是 Dictionary / Array 容器且有子项报错，将容器的名字加入报错列表
                if (TypeName == "Dictionary" || TypeName == "Array")
                {
                    string containerName = string.IsNullOrEmpty(DisplayName) ? PropertyName : DisplayName;
                    if (!errorDisplayNames.Contains(containerName))
                    {
                        errorDisplayNames.Add(containerName);
                    }
                }
            }
        }

        if (firstErrorVm != null) IsExpanded = true;
        return firstErrorVm;
    }

    // 增加一个无参版本方便调用
    public bool Validate() => Validate(out _);

    private BsonValue ConvertToBsonValue(object? value, string typeName)
    {
        try
        {
            if (value == null) return BsonValue.Null;

            return typeName switch
            {
                "String" => new BsonValue(value.ToString()),
                "Int32" => new BsonValue(Convert.ToInt32(value)),
                "Int64" => new BsonValue(Convert.ToInt64(value)),
                "Double" => new BsonValue(Convert.ToDouble(value)),
                "Boolean" => new BsonValue(Convert.ToBoolean(value)),
                _ => new BsonValue(value.ToString())
            };
        }
        catch { return BsonValue.Null; }
    }

    #endregion
}
