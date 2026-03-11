using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LiteDB;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// A wrapper around BsonDocument to support dynamic data binding in Avalonia DataGrid.
/// It uses a dictionary-like accessor to let DataGrid columns read and write values.
/// </summary>
public class BsonDocumentWrapper : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly BsonDocument _document;
    private readonly Action<BsonDocumentWrapper>? _onModified;
    private readonly Dictionary<string, BsonValue> _pendingChanges = new();
    private readonly Dictionary<string, string?> _fieldErrors = new();
    private BsonValue _originalId;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Indicates whether this wrapper has any unsaved modifications.
    /// </summary>
    public bool IsModified => _pendingChanges.Count > 0;

    /// <summary>
    /// 记录数据从数据库加载时的初始 ID。
    /// 如果是新建数据尚未入库，则为 BsonValue.Null。
    /// </summary>
    public BsonValue OriginalId => _originalId;

    public BsonDocumentWrapper(BsonDocument document, Action<BsonDocumentWrapper>? onModified = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _onModified = onModified;
        // 初始化时捕获当前 ID，作为追踪旧记录的依据
        _originalId = _document.TryGetValue("_id", out var id) ? id : BsonValue.Null;
    }

    /// <summary>
    /// 在数据库保存成功后调用，将当前 ID 标记为新的“原始 ID”。
    /// </summary>
    public void SyncOriginalId()
    {
        _originalId = _document.TryGetValue("_id", out var id) ? id : BsonValue.Null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalId)));
    }

    /// <summary>
    /// Checks if a specific field has unsaved changes.
    /// </summary>
    public bool IsFieldModified(string key)
    {
        return _pendingChanges.ContainsKey(key);
    }

    public void AcceptChanges()
    {
        if (_pendingChanges.Count == 0) return;

        foreach (var kvp in _pendingChanges)
        {
            _document[kvp.Key] = kvp.Value;
        }
        _pendingChanges.Clear();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        RefreshAll();
    }

    /// <summary>
    /// Discards all pending changes and reverts to the original BsonDocument state.
    /// </summary>
    public void RejectChanges()
    {
        if (_pendingChanges.Count == 0) return;

        _pendingChanges.Clear();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        RefreshAll();
    }

    public BsonDocument Document => _document;

    /// <summary>
    /// 返回将待存更改并入地合并的完整文档快照，不修改原始内存。
    /// 用于创建编辑弹窗时的克隆源。
    /// </summary>
    public BsonDocument GetMergedDocument()
    {
        var merged = new BsonDocument(_document);
        foreach (var kvp in _pendingChanges)
            merged[kvp.Key] = kvp.Value;
        return merged;
    }

    /// <summary>
    /// 获取某个字段的原始 BsonValue，优先从待存更改中取，用于需要对透实际数据进行操作的场景。
    /// </summary>
    public BsonValue GetRawValue(string key)
    {
        if (_pendingChanges.TryGetValue(key, out var pending)) return pending;
        if (_document.TryGetValue(key, out var orig)) return orig;
        return BsonValue.Null;
    }

    /// <summary>
    /// 将改动后的 BsonValue 写回待存列表并发出下载刷新通知。
    /// </summary>
    public void SetRawValueAndNotify(string key, BsonValue value)
    {
        if (_document.TryGetValue(key, out var dbVal) && dbVal == value)
            _pendingChanges.Remove(key);
        else
            _pendingChanges[key] = value;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        _onModified?.Invoke(this);
    }

    #region 错误处理 (INotifyDataErrorInfo)

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => _fieldErrors.Any();

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return Enumerable.Empty<string>();

        // DataGrid 绑定针对索引器可能产生 "Item[PropName]" 或 "[PropName]"
        string key = propertyName;
        if (propertyName.StartsWith("Item["))
            key = propertyName.Substring(5, propertyName.Length - 6);
        else if (propertyName.StartsWith("[") && propertyName.EndsWith("]"))
            key = propertyName.Substring(1, propertyName.Length - 2);

        if (_fieldErrors.TryGetValue(key, out var error))
            return new[] { error };

        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// 设置某个字段的错误消息。
    /// </summary>
    public void SetError(string key, string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            _fieldErrors.Remove(key);
        else
            _fieldErrors[key] = errorMessage;

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs($"Item[{key}]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
    }

    /// <summary>
    /// 获取某个字段是否存在错误。
    /// </summary>
    public bool HasFieldError(string key) => _fieldErrors.ContainsKey(key);

    /// <summary>
    /// 清除所有字段错误。
    /// </summary>
    public void ClearAllErrors()
    {
        var keys = _fieldErrors.Keys.ToList();
        _fieldErrors.Clear();
        foreach (var key in keys)
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs($"Item[{key}]"));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
    }

    #endregion

    /// <summary>
    /// 当内容通过外部直接修改了（不经过索引器 setter）时，主动标记该字段为脚数据并通知 UI。
    /// </summary>
    public void MarkModifiedAndNotify(string key)
    {
        // 为外部就地修改的 Array/Document 确保它也被记入脚数据列表
        if (!_pendingChanges.ContainsKey(key))
        {
            // 获取共享引用（如果存在）直接用它
            if (_document.TryGetValue(key, out var orig))
                _pendingChanges[key] = orig; // 存结构引用，不是剥离副本
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        _onModified?.Invoke(this);
    }

    /// <summary>
    /// Forces a refresh of all properties bound to this wrapper 
    /// (useful when the underlying BsonDocument is replaced or heavily modified externally).
    /// </summary>
    public void RefreshAll()
    {
        // 传递 string.Empty 告诉 UI 本对象的所有属性全脏需要重画
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        // 显式通知索引器属性名变更
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
    }

    // Indexer for DataGrid TextColumn Binding (e.g., Binding[Name]).
    public string? this[string key]
    {
        get
        {
            // 优先查找脏值
            BsonValue? bsonValue = null;
            if (_pendingChanges.TryGetValue(key, out var pendingVal))
            {
                bsonValue = pendingVal;
            }
            else if (_document.TryGetValue(key, out var origVal))
            {
                bsonValue = origVal;
            }
            else
            {
                return null;
            }

            return ExtractValue(bsonValue);
        }
        set
        {
            var oldVal = this[key];
            if (string.Equals(oldVal, value)) return;

            var newVal = ConvertToBsonValue(value, _document[key]?.Type ?? BsonType.String);

            // 如果填改回来的值恰好与最早最原本的库里的老值内容一致了
            if (_document.TryGetValue(key, out var dbVal) && dbVal == newVal)
            {
                _pendingChanges.Remove(key);
            }
            else
            {
                _pendingChanges[key] = newVal;
            }

            // 修改值时，自动清除该字段的旧错误提示
            if (_fieldErrors.Remove(key))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs($"Item[{key}]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
            }

            // 通知单个值更新以重绘高亮
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
            // 通知整体脏数据标记
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));

            // Call up to tell VM we have changes
            _onModified?.Invoke(this);
        }
    }

    private string? ExtractValue(BsonValue bsonValue)
    {
        if (bsonValue.IsNull) return null;

        return bsonValue.Type switch
        {
            BsonType.String => bsonValue.AsString,
            BsonType.Int32 => bsonValue.AsInt32.ToString(),
            BsonType.Int64 => bsonValue.AsInt64.ToString(),
            BsonType.Double => bsonValue.AsDouble.ToString(),
            BsonType.Boolean => bsonValue.AsBoolean.ToString(),
            BsonType.DateTime => bsonValue.AsDateTime.ToString("o"),
            BsonType.ObjectId => bsonValue.AsObjectId.ToString(),
            BsonType.Document => LiteDB.JsonSerializer.Serialize(bsonValue),
            BsonType.Array => LiteDB.JsonSerializer.Serialize(bsonValue),
            _ => bsonValue.RawValue?.ToString()
        };
    }

    public BsonValue ConvertToBsonValue(string? stringVal, BsonType targetType)
    {
        if (string.IsNullOrEmpty(stringVal)) return BsonValue.Null;

        try
        {
            return targetType switch
            {
                BsonType.String => new BsonValue(stringVal),
                BsonType.Int32 => new BsonValue(Convert.ToInt32(stringVal)),
                BsonType.Int64 => new BsonValue(Convert.ToInt64(stringVal)),
                BsonType.Double => new BsonValue(Convert.ToDouble(stringVal)),
                BsonType.Boolean => new BsonValue(Convert.ToBoolean(stringVal)),
                BsonType.ObjectId => new BsonValue(new ObjectId(stringVal)),
                BsonType.Document => TryParseAsBson(stringVal, BsonType.Document),
                BsonType.Array => TryParseAsBson(stringVal, BsonType.Array),
                _ => new BsonValue(stringVal)
            };
        }
        catch
        {
            return new BsonValue(stringVal);
        }
    }

    private BsonValue TryParseAsBson(string jsonContent, BsonType targetType)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return targetType == BsonType.Array ? new BsonArray() : new BsonDocument();

        try
        {
            var parsed = LiteDB.JsonSerializer.Deserialize(jsonContent);

            if (targetType == BsonType.Array && !parsed.IsArray) return parsed;
            if (targetType == BsonType.Document && !parsed.IsDocument) return parsed;

            return parsed;
        }
        catch
        {
            throw new InvalidOperationException($"Invalid JSON format for {targetType}.");
        }
    }
}
