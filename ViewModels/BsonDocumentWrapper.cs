using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// BsonDocument 的包装类，旨在支持 Avalonia DataGrid 的动态数据绑定。
/// 它通过索引器实现灵活的数据访问，并内部追踪所有待存的更改（新增、修改或字段删除），
/// 从而支持“撤销修改”和“批量保存”功能。
/// </summary>
public class BsonDocumentWrapper : INotifyPropertyChanged, INotifyDataErrorInfo
{
    private readonly BsonDocument _document;
    private readonly Action<BsonDocumentWrapper>? _onModified;
    private readonly Dictionary<string, BsonValue> _pendingChanges = new();
    private readonly HashSet<string> _deletedFields = new();
    private readonly Dictionary<string, string?> _fieldErrors = new();
    private BsonValue _originalId;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 获取一个值，指示该包装器当前是否有任何未保存的修改（包括值变更或字段删除）。
    /// </summary>
    public bool IsModified => _pendingChanges.Count > 0 || _deletedFields.Count > 0;

    /// <summary>
    /// 获取被标记为待删除的字段列表。
    /// </summary>
    public IEnumerable<string> DeletedFields => _deletedFields;

    /// <summary>
    /// 记录数据从数据库加载时的初始 _id。
    /// 如果是新建数据尚未入库，则为 BsonValue.Null。
    /// </summary>
    public BsonValue OriginalId => _originalId;

    public BsonDocumentWrapper(BsonDocument document, Action<BsonDocumentWrapper>? onModified = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _onModified = onModified;
        // 初始化时捕获当前 ID，作为追踪旧记录的依据，防止因 ID 修改导致无法定位原始文档
        _originalId = _document.TryGetValue("_id", out var id) ? id : BsonValue.Null;
    }

    /// <summary>
    /// 在数据库保存成功后调用，将当前最新的 ID 标记为新的“原始 ID”。
    /// </summary>
    public void SyncOriginalId()
    {
        _originalId = _document.TryGetValue("_id", out var id) ? id : BsonValue.Null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalId)));
    }

    /// <summary>
    /// 还原所有未保存的修改，清空待存更新列表和待删除字段列表。
    /// </summary>
    public void ResetChanges()
    {
        _pendingChanges.Clear();
        _deletedFields.Clear();
        _fieldErrors.Clear();
        
        // 通知 UI 所有字段值可能已回滚
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalId)));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(null)); // 清除所有校验错误
    }

    /// <summary>
    /// 检查特定字段是否有尚未保存的更改。
    /// </summary>
    public bool IsFieldModified(string key)
    {
        return _pendingChanges.ContainsKey(key);
    }

    /// <summary>
    /// 接受所有更改，将待存列表中的值正式写入底层的 BsonDocument 实例并清空追踪状态。
    /// </summary>
    public void AcceptChanges()
    {
        if (_pendingChanges.Count == 0 && _deletedFields.Count == 0) return;

        foreach (var kvp in _pendingChanges)
        {
            _document[kvp.Key] = kvp.Value;
        }
        _pendingChanges.Clear();

        foreach (var field in _deletedFields)
        {
            _document.Remove(field);
        }
        _deletedFields.Clear();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        RefreshAll();
    }

    /// <summary>
    /// 丢弃所有未保存的更改，并触发 UI 刷新。
    /// </summary>
    public void RejectChanges()
    {
        if (_pendingChanges.Count == 0 && _deletedFields.Count == 0) return;

        _pendingChanges.Clear();
        _deletedFields.Clear();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        RefreshAll();
    }

    /// <summary>
    /// 将指定字段标记为待从 BsonDocument 中移除。
    /// </summary>
    public void RemoveField(string key)
    {
        if (key == "_id") return;

        bool changed = false;
        if (_document.ContainsKey(key) || _pendingChanges.ContainsKey(key))
        {
            _deletedFields.Add(key);
            _pendingChanges.Remove(key);
            changed = true;
        }

        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
            _onModified?.Invoke(this);
        }
    }

    /// <summary>
    /// 针对包装器实现的严格 Schema 应用逻辑。
    /// 该方法会识别 Schema 变更并自动将其转化为本包装器的“修改”或“删除”指令，
    /// 从而确保能够触发“保存”按钮并持久化到数据库。
    /// </summary>
    public bool ApplySchemaStrictly(LiteDBEditor.Services.SchemaParserService parser, SchemaData schema)
    {
        // 1. 获取合并了当前待存更改后的快照视图
        var merged = GetMergedDocument();
        
        // 2. 利用 parser 的启发式逻辑识别字段定义的变化
        var allowedNames = new HashSet<string>(schema.Properties.Select(p => p.Name), StringComparer.Ordinal); // 强规则：区分大小写
        
        var currentKeys = merged.Keys.ToList();
        var extraKeys = currentKeys.Where(k => k != "_id" && !allowedNames.Contains(k)).ToList();
        var missingNames = schema.Properties.Select(p => p.Name).Where(n => n != "_id" && !merged.ContainsKey(n)).ToList();

        bool hasChanged = false;

        // --- 智能字段迁移 (Smart Migration) ---
        // 如果冗余字段名和缺失字段名仅有大小写差异，则自动视为重命名并迁移数据
        if (extraKeys.Count == 1 && missingNames.Count == 1 && 
            string.Equals(extraKeys[0], missingNames[0], StringComparison.OrdinalIgnoreCase))
        {
            var oldKey = extraKeys[0];
            var newKey = missingNames[0];
            var val = merged[oldKey];
            
            // 标记旧字段删除，新字段赋值
            RemoveField(oldKey);
            SetRawValueAndNotify(newKey, val);
            hasChanged = true;
        }
        else
        {
            // --- 冗余清理：移除不在 Schema 范围内的所有多余字段 ---
            foreach (var extra in extraKeys)
            {
                RemoveField(extra);
                hasChanged = true;
            }
        }

        return hasChanged;
    }

    /// <summary>
    /// 获取包装的原始 BsonDocument 实例。
    /// </summary>
    public BsonDocument Document => _document;

    /// <summary>
    /// 返回将当前待存更改与原始文档合并后的完整快照，该操作不会修改任何原始状态。
    /// 常用于在打开编辑详情窗口时提供一个独立的克隆副本。
    /// </summary>
    public BsonDocument GetMergedDocument()
    {
        var merged = new BsonDocument(_document);
        foreach (var kvp in _pendingChanges)
            merged[kvp.Key] = kvp.Value;
        
        foreach (var field in _deletedFields)
            merged.Remove(field);
            
        return merged;
    }

    /// <summary>
    /// 获取某个字段当前的 BsonValue 原始值。
    /// 逻辑：优先从待存更改列表中取，若无则从底层文档取。
    /// </summary>
    public BsonValue GetRawValue(string key)
    {
        if (_pendingChanges.TryGetValue(key, out var pending)) return pending;
        if (_document.TryGetValue(key, out var orig)) return orig;
        return BsonValue.Null;
    }

    /// <summary>
    /// 直接将某个 BsonValue 写入待存列表，并通知 UI 刷新该字段。
    /// </summary>
    public void SetRawValueAndNotify(string key, BsonValue value)
    {
        // 如果设定的值与数据库原始值一致，则从待存列表中移除（表示未修改）
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

    /// <summary>
    /// 指示该包装器当前是否有任何校验错误。
    /// </summary>
    public bool HasErrors => _fieldErrors.Any();

    /// <summary>
    /// 获取指定属性名的所有错误。
    /// </summary>
    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return Enumerable.Empty<string>();

        // 处理 DataGrid 可能生成的各种形式的索引器属性名
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
    /// 为指定字段设置或清除错误消息。
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
    /// 检查某个字段是否存在错误。
    /// </summary>
    public bool HasFieldError(string key) => _fieldErrors.ContainsKey(key);

    /// <summary>
    /// 清除该包装器的所有字段错误信息。
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
    /// 手动标记某个字段已修改。
    /// 当在外部直接通过引用修改了 BsonDocument（如修改嵌套数组/文档）时，
    /// 需要调用此方法来确何更改被追踪并通知 UI 刷新。
    /// </summary>
    public void MarkModifiedAndNotify(string key)
    {
        if (!_pendingChanges.ContainsKey(key))
        {
            if (_document.TryGetValue(key, out var orig))
                _pendingChanges[key] = orig; 
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
        _onModified?.Invoke(this);
    }

    /// <summary>
    /// 强制刷新此包装器的所有绑定属性。
    /// </summary>
    public void RefreshAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
    }

    /// <summary>
    /// 提供给 DataGrid Column 使用的索引器绑定。
    /// 逻辑：get 时自动将 BsonValue 转换为字符串；set 时尝试按目标类型反向解析回 BsonValue。
    /// </summary>
    public string? this[string key]
    {
        get
        {
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

            // 智能回滚：如果修改回了数据库原始值，则从待存列表中移除该字段
            if (_document.TryGetValue(key, out var dbVal) && dbVal == newVal)
            {
                _pendingChanges.Remove(key);
            }
            else
            {
                _pendingChanges[key] = newVal;
            }

            // 修改值时自动清除之前的旧错误
            if (_fieldErrors.Remove(key))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs($"Item[{key}]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"Item[{key}]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));

            _onModified?.Invoke(this);
        }
    }

    /// <summary>
    /// 将 BsonValue 解析为适合 UI 编辑器显示的字符串。
    /// </summary>
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

    /// <summary>
    /// 将字符串输入尝试解析为指定的 BsonType。
    /// </summary>
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
            // 解析失败时作为普通字符串处理，防止崩溃
            return new BsonValue(stringVal);
        }
    }

    /// <summary>
    /// 尝试将字符串解析为 JSON，并验证是否为预期的 BsonDocument 或 BsonArray。
    /// </summary>
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
