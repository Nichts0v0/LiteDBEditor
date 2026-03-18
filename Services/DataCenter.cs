using LiteDBEditor.Services;

namespace LiteDBEditor;

/// <summary>
/// 全局数据与服务中心，作为单例服务的静态访问入口，方便在各个 ViewModel 和 View 中调用核心逻辑。
/// </summary>
public static class DataCenter
{
    /// <summary>
    /// 获取 LiteDB 数据库交互服务。
    /// </summary>
    public static DatabaseService Database { get; } = new();

    /// <summary>
    /// 获取 Schema 绑定管理服务。
    /// </summary>
    public static SchemaBindingService Bindings { get; } = new();

    /// <summary>
    /// 获取 Schema 元数据持久化服务。
    /// </summary>
    public static SchemaMetadataService Metadata { get; } = new();
}
