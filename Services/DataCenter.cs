using LiteDBEditor.Services;

namespace LiteDBEditor;

/// <summary>
/// 全局数据与服务中心，提供静态访问点。
/// </summary>
public static class DataCenter
{
    public static DatabaseService Database { get; } = new();
    public static SchemaBindingService Bindings { get; } = new();
    public static SchemaMetadataService Metadata { get; } = new();
}
