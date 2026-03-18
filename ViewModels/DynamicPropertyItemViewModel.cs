using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 动态属性编辑项的 ViewModel，代表编辑器中的一行输入项。
/// 它支持递归结构，能够处理简单类型（字符串、数字、布尔）以及复杂类型（嵌套文档、数组、字典）。
/// </summary>
public partial class DynamicPropertyItemViewModel : ViewModelBase
{
    #region 属性基础信息与状态

    /// <summary>
    /// 程序内部使用的属性名称（如字段名）。
    /// </summary>
    [ObservableProperty]
    private string _propertyName = string.Empty;

    /// <summary>
    /// UI 显示使用的友好名称。
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 字段的类型名称（如 String, Int32, Array, Document 等）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeNameDisplay))]
    private string _typeName = "String";

    /// <summary>
    /// 获取用于 UI 展示的友好类型描述字符串。
    /// </summary>
    public string TypeNameDisplay => PropertySchema?.GetFriendlyTypeString() ?? TypeName;

    /// <summary>
    /// 是否为必填项。
    /// </summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>
    /// 是否为只读项。
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>
    /// 当前字段的校验错误消息。
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 获取完整的 SchemaProperty 元数据，包含子元素结构和嵌套属性定义。
    /// </summary>
    public SchemaProperty? PropertySchema { get; private set; }

    /// <summary>
    /// 获取或设置查重回调，用于检测 ID 等唯一性约束。
    /// </summary>
    public Func<string, BsonValue, bool>? IdDuplicateCheckFunc { get; set; }

    private string? _originalKey;
    private object? _originalValue;

    #endregion

    #region 复杂类型支持 (递归)

    /// <summary>
    /// 复杂项（Array/Document）是否处于展开状态。
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 嵌套的子属性编辑项集合。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DynamicPropertyItemViewModel> _children = new();

    /// <summary>
    /// 标记当前项是否为 Dictionary 的条目，这将激活 Key 输入框的显示。
    /// </summary>
    [ObservableProperty]
    private bool _isDictionaryItem;

    /// <summary>
    /// Dictionary 模式下的 Key 值，支持双向绑定并包含查重逻辑。
    /// </summary>
    [ObservableProperty]
    private string? _dictItemKey;

    #endregion

    #region 属性值管理

    private BsonDocument? _parentDocument;
    private BsonArray? _parentArray;
    private string? _documentKey;
    private int _arrayIndex = -1;

    private Action<DynamicPropertyItemViewModel>? _onRemoveRequested;

    /// <summary>
    /// 指示该项是否允许被删除（通常由父容器管理）。
    /// </summary>
    public bool CanRemove => _onRemoveRequested != null;

    /// <summary>
    /// 指示该项是否允许添加子项（仅限 Array 和 Dictionary）。
    /// </summary>
    public bool CanAddChildren => TypeName == "Array" || TypeName == "Dictionary";

    /// <summary>
    /// 获取该字段当前对应的原始 BsonValue。
    /// 如果是嵌套结构，则返回其 BsonArray 或 BsonDocument 引用。
    /// </summary>
    public BsonValue? CurrentBsonValue { get; private set; }

    /// <summary>
    /// 绑定的输入值，对应基础类型的直接编辑。
    /// </summary>
    [ObservableProperty]
    private object? _value;

    [ObservableProperty]
    private bool _isStringEditorVisible;

    /// <summary>
    /// 指示数字编辑器（支持 Int32/Int64/Double）是否可见。
    /// </summary>
    [ObservableProperty]
    private bool _isNumericEditorVisible;

    [ObservableProperty]
    private bool _isBooleanEditorVisible;

    [ObservableProperty]
    private bool _isComplexEditorVisible;

    /// <summary>
    /// 复杂类型的预览文本（如 "[10 项]" 或 "[嵌套对象]"）。
    /// </summary>
    [ObservableProperty]
    private string _complexTypePreview = string.Empty;

    /// <summary>
    /// 将此项初始化为 BsonDocument 中的一个属性字段。
    /// </summary>
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

    /// <summary>
    /// 将此项初始化为 BsonArray 中的一个索引元素。
    /// </summary>
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

    /// <summary>
    /// 将此项初始化为 Dictionary 容器中的一个键值对条目。
    /// </summary>
    public void InitializeAsDictionaryItem(BsonDocument parent, string key, SchemaProperty valueSchema, Action<DynamicPropertyItemViewModel>? onRemove = null)
    {
        _parentDocument = parent;
        _documentKey = key;
        DictItemKey = key;
        PropertySchema = valueSchema;
        _onRemoveRequested = onRemove;

        IsDictionaryItem = true;
        PropertyName = key;
        DisplayName = string.Empty; // 字典项通过 Key 输入框显示名称
        TypeName = valueSchema.TypeName;

        var bsonVal = parent.TryGetValue(key, out var val) ? val : BsonValue.Null;
        _originalValue = bsonVal.RawValue;
        UpdateVisibilityAndValue(valueSchema, bsonVal);
    }

    /// <summary>
    /// 当输入值改变时，实时进行基础业务逻辑校验（如 ID 查重）并写回底层存储。
    /// </summary>
    partial void OnValueChanged(object? value)
    {
        ErrorMessage = null;
        
        // 针对主键 _id 字段的实时查重校验
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

    /// <summary>
    /// 当字典项的 Key 改变时，尝试执行底层的键名重命名操作。
    /// 如果新 Key 冲突或无效，则会自动回滚 UI 值。
    /// </summary>
    partial void OnDictItemKeyChanged(string? value)
    {
        if (!IsDictionaryItem || _parentDocument == null || _documentKey == null) return;

        // 如果新值等于当前正式生效的 Key，则不做任何处理
        if (value == _documentKey) return;

        ErrorMessage = null;
        
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = "键名不能为空";
            DictItemKey = _documentKey; 
            return;
        }

        // 检查同一个 BsonDocument 内部是否已存在同名 Key
        var collision = _parentDocument.Keys.Contains(value);
        if (collision)
        {
            ErrorMessage = $"键名 '{value}' 已存在";
            DictItemKey = _documentKey;
            return;
        }

        // 校验通过：执行物理重命名（移动数据并更新 Key 指针）
        var currentVal = _parentDocument[_documentKey];
        _parentDocument.Remove(_documentKey);
        _parentDocument[value] = currentVal;
        _documentKey = value;
        PropertyName = value;
    }

    /// <summary>
    /// 将 UI 修改后的值同步写回到绑定的底层 BsonDocument 或 BsonArray 中。
    /// </summary>
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

    /// <summary>
    /// 根据字段类型决定渲染哪个编辑器，并初始化其关联的子项（递归场景）。
    /// </summary>
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
                    // 若底层类型不匹配，则强制纠正为 BsonDocument
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
                    // 纠正底层数据类型为 BsonArray
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

    /// <summary>
    /// 展开或折叠嵌套内容。
    /// </summary>
    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    /// <summary>
    /// 为 Array 添加一个新元素，或为 Dictionary 添加一个新的键值对。
    /// 包含简单的“空项检查”，防止生成大量垃圾空白项。
    /// </summary>
    [RelayCommand]
    private void AddChild()
    {
        if (TypeName == "Array" && CurrentBsonValue is BsonArray arr)
        {
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

            // 自动生成一个不冲突的默认 Key
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

    /// <summary>
    /// 内部逻辑：移除指定的子项并同步刷新后续索引。
    /// </summary>
    private void RemoveChildInternal(DynamicPropertyItemViewModel child)
    {
        if (TypeName == "Array" && CurrentBsonValue is BsonArray arr)
        {
            int idx = Children.IndexOf(child);
            if (idx >= 0)
            {
                arr.RemoveAt(idx);
                Children.RemoveAt(idx);
                // 关键点：对于数组，删除后必须对后续所有子项执行重新初始化以修正显示的索引值
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

    /// <summary>
    /// 请求父级容器将自己从列表中移除。
    /// </summary>
    [RelayCommand]
    private void RemoveSelf() => _onRemoveRequested?.Invoke(this);

    /// <summary>
    /// 启发式检查某个编辑项是否处于“空”状态。
    /// </summary>
    private bool IsItemEmpty(DynamicPropertyItemViewModel item)
    {
        if (item.IsComplexEditorVisible) return false;
        if (item.TypeName == "Int32" || item.TypeName == "Int64" || item.TypeName == "Double") return false;
        var v = item.Value?.ToString();
        return string.IsNullOrEmpty(v);
    }

    /// <summary>
    /// 根据类型名创建 BsonValue 的默认初始值。
    /// </summary>
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

    /// <summary>
    /// 重新生成容器类字段（数组/字典）的预览摘要文本。
    /// </summary>
    public void RefreshComplexPreview()
    {
        if (CurrentBsonValue == null) return;
        if (CurrentBsonValue.IsArray)
            ComplexTypePreview = $"[{Children.Count} 项]";
        else if (CurrentBsonValue.IsDocument)
            ComplexTypePreview = $"[{Children.Count} 项键值对]";
    }

    /// <summary>
    /// 执行本级及其所有子项的完整校验逻辑。
    /// </summary>
    /// <param name="error">返回的第一个错误描述</param>
    /// <returns>校验是否通过</returns>
    public bool Validate(out string? error)
    {
        error = null;

        // 优先检查由 UI 触发的实时校验错误信息
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

        // 递归校验子项
        foreach (var child in Children)
        {
            if (!child.Validate(out error))
            {
                IsExpanded = true;
                return false;
            }
            
            // 针对字典类型的重名校验
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
    /// 递归收集当前项及其所有子项中的校验错误。
    /// </summary>
    /// <param name="errorDisplayNames">用于存放所有错误字段显示名的列表</param>
    /// <returns>第一个发现错误的项的 ViewModel，用于 UI 滚动定位</returns>
    public DynamicPropertyItemViewModel? CollectAllErrors(System.Collections.Generic.List<string> errorDisplayNames)
    {
        // 强制执行一轮全量同步校验
        Validate(out _);

        DynamicPropertyItemViewModel? firstErrorVm = null;

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            // 对于非字典条目，将其显示名加入汇总列表
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

                // 如果当前项是容器且子项报错，也将容器名加入错误列表
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

    /// <summary>
    /// 校验逻辑的简易无参重载。
    /// </summary>
    public bool Validate() => Validate(out _);

    /// <summary>
    /// 执行最终的数据类型转换，将 object 转换为 BsonValue。
    /// </summary>
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
