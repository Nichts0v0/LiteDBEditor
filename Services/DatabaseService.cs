using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;

namespace LiteDBEditor.Services;

/// <summary>
/// 负责与LiteDB数据库文件进行底层交互的核心服务类
/// </summary>
public class DatabaseService : IDisposable
{
    #region 数据库连接管理

    private LiteDatabase? _database;
    public string? CurrentDbPath { get; private set; }

    /// <summary>
    /// 当前是否打开了数据库实例
    /// </summary>
    public bool IsOpen => _database != null;

    /// <summary>
    /// 打开指定的LiteDB数据库文件
    /// </summary>
    public void OpenDatabase(string path)
    {
        CloseDatabase();
        try
        {
            _database = new LiteDatabase(path);
            CurrentDbPath = path;
        }
        catch (Exception ex)
        {
            throw new Exception($"无法打开数据库文件: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 关闭当前数据库连接
    /// </summary>
    public void CloseDatabase()
    {
        if (_database != null)
        {
            _database.Dispose();
            _database = null;
            CurrentDbPath = null;
        }
    }

    public void Dispose()
    {
        CloseDatabase();
    }

    #endregion

    #region 核心集合与数据操作

    public void CreateCollection(string collectionName)
    {
        if (_database == null) return;
        var collection = _database.GetCollection(collectionName);

        // 为了确保能产生物理文件级别的追踪记录（而不仅仅是代码对象），在此立即塞入后删除
        var id = ObjectId.NewObjectId();
        var fake = new BsonDocument();
        fake["_id"] = id;
        collection.Insert(fake);
        collection.Delete(id);
    }

    public void DropCollection(string collectionName)
    {
        if (_database == null) return;
        _database.DropCollection(collectionName);
    }

    /// <summary>
    /// 获取当前数据库中的所有集合(表)的名称
    /// </summary>
    public List<string> GetCollectionNames()
    {
        if (_database == null) return new List<string>();

        return _database.GetCollectionNames().ToList();
    }

    /// <summary>
    /// 获取指定集合下的所有Bson文档数据
    /// </summary>
    public List<BsonDocument> GetAllDocuments(string collectionName)
    {
        if (_database == null) return new List<BsonDocument>();

        var collection = _database.GetCollection(collectionName);
        return collection.Query().OrderBy("_id").ToList();
    }

    /// <summary>
    /// 获取指定集合的数据，支持基础分页(若日后需要)
    /// </summary>
    public List<BsonDocument> GetDocuments(string collectionName, int skip, int limit)
    {
        if (_database == null) return new List<BsonDocument>();

        var collection = _database.GetCollection(collectionName);
        return collection.Query().OrderBy("_id").Offset(skip).Limit(limit).ToList();
    }

    /// <summary>
    /// 更新现有的数据文档
    /// </summary>
    public bool UpdateDocument(string collectionName, BsonDocument document)
    {
        if (_database == null) return false;

        var collection = _database.GetCollection(collectionName);
        return collection.Update(document);
    }

    /// <summary>
    /// 更新或插入数据文档
    /// </summary>
    public bool UpsertDocument(string collectionName, BsonDocument document)
    {
        if (_database == null) return false;

        var collection = _database.GetCollection(collectionName);
        return collection.Upsert(document);
    }

    /// <summary>
    /// 插入一条新的数据文档
    /// </summary>
    public void InsertDocument(string collectionName, BsonDocument document)
    {
        if (_database == null) return;

        var collection = _database.GetCollection(collectionName);
        collection.Insert(document);
    }

    /// <summary>
    /// 删除指定_id的文档
    /// </summary>
    public bool DeleteDocument(string collectionName, BsonValue id)
    {
        if (_database == null) return false;

        var collection = _database.GetCollection(collectionName);
        return collection.Delete(id);
    }

    #endregion
}
