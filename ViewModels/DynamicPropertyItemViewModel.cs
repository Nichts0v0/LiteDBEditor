using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private string _typeName = "String";

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

    #endregion

    #region 属性值管理

    // 存储底层 BsonDocument 或者 BsonArray 和它对应的 Key 或者 Index
    private BsonDocument? _parentDocument;
    private BsonArray? _parentArray;
    private string? _documentKey;
    private int _arrayIndex = -1;

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

    public void InitializeWithDocument(BsonDocument parent, string key, SchemaProperty schema)
    {
        _parentDocument = parent;
        _documentKey = key;
        PropertySchema = schema;  // 保存完整 Schema 供子编辑器使用

        PropertyName = schema.Name;
        DisplayName = $"{schema.DisplayName} ({schema.TypeName})";
        TypeName = schema.TypeName;
        IsRequired = schema.IsRequired;

        var bsonVal = parent.TryGetValue(key, out var val) ? val : BsonValue.Null;

        UpdateVisibilityAndValue(schema, bsonVal);
    }

    public void InitializeWithArray(BsonArray parent, int index, SchemaProperty schema)
    {
        _parentArray = parent;
        _arrayIndex = index;
        PropertySchema = schema;  // 保存 Schema

        PropertyName = $"Item [{index}]";
        DisplayName = $"Item [{index}] ({schema.TypeName})";
        TypeName = schema.TypeName;

        var bsonVal = parent[index];
        UpdateVisibilityAndValue(schema, bsonVal);
    }

    partial void OnValueChanged(object? value)
    {
        // 只要用户修改了值，就清除之前的错误提示
        ErrorMessage = null;

        if (_parentDocument != null && _documentKey != null)
        {
            _parentDocument[_documentKey] = ConvertToBsonValue(value, TypeName);
        }
        else if (_parentArray != null && _arrayIndex >= 0)
        {
            _parentArray[_arrayIndex] = ConvertToBsonValue(value, TypeName);
        }
    }

    private void UpdateVisibilityAndValue(SchemaProperty schema, BsonValue bsonVal)
    {
        CurrentBsonValue = bsonVal;
        IsStringEditorVisible = false;
        IsNumericEditorVisible = false;
        IsBooleanEditorVisible = false;
        IsComplexEditorVisible = false;

        switch (schema.TypeName)
        {
            case "String":
                Value = bsonVal.IsString ? bsonVal.AsString : bsonVal.RawValue?.ToString() ?? "";
                IsStringEditorVisible = true;
                break;

            case "Int32":
            case "Int64":
                // 先赋值再设可见：防止 TextBox 出现时 Value 仍为 null，
                // 导致 TwoWay 绑定将空字符串写回文档
                Value = bsonVal.IsNumber ? bsonVal.AsInt32.ToString() : "0";
                IsNumericEditorVisible = true;
                break;

            case "Double":
                Value = bsonVal.IsNumber ? bsonVal.AsDouble.ToString() : "0";
                IsNumericEditorVisible = true;
                break;

            case "Boolean":
                Value = bsonVal.IsBoolean ? bsonVal.AsBoolean : false;
                IsBooleanEditorVisible = true;
                break;

            case "Dictionary":
                IsComplexEditorVisible = true;
                ComplexTypePreview = bsonVal.IsDocument
                    ? $"[{bsonVal.AsDocument.Count} 项键值对]"
                    : "[字典]";
                break;

            case "Document":
                IsComplexEditorVisible = true;
                ComplexTypePreview = "[嵌套对象]";
                break;

            case "Array":
                IsComplexEditorVisible = true;
                ComplexTypePreview = bsonVal.IsArray
                    ? $"[{bsonVal.AsArray.Count} 项]"
                    : "[列表]";
                break;

            default:
                IsStringEditorVisible = true;
                Value = bsonVal.RawValue?.ToString() ?? "";
                break;
        }
    }

    /// <summary>
    /// 子编辑器（CollectionEditorWindow）关闭后，重新读取底层数据更新预览文字（如"[3 项]"）。
    /// 仅对复杂类型有效。
    /// </summary>
    public void RefreshComplexPreview()
    {
        if (CurrentBsonValue == null) return;
        if (CurrentBsonValue.IsArray)
            ComplexTypePreview = $"[{CurrentBsonValue.AsArray.Count} 项]";
        else if (CurrentBsonValue.IsDocument)
            ComplexTypePreview = $"[{CurrentBsonValue.AsDocument.Count} 项键值对]";
    }

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
        catch (Exception ex)
        {
            Console.WriteLine($"转换値异常: {ex.Message}");
            return BsonValue.Null;
        }
    }

    #endregion
}
