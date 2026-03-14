using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using LiteDBEditor.Models;

namespace LiteDBEditor.ViewModels;

public partial class DynamicPropertiesViewModel : ViewModelBase
{
    #region 弹窗状态与集合

    [ObservableProperty]
    private string _title = "编辑文档";

    [ObservableProperty]
    private ObservableCollection<DynamicPropertyItemViewModel> _properties = new();

    [ObservableProperty]
    private string? _windowErrorMessage;

    [ObservableProperty]
    private string? _contextPath;

    private BsonDocument? _targetBsonDocument;
    private Func<BsonDocument, Task<bool>>? _onSaveAsync;
    private SchemaData? _schemaData;

    #endregion

    #region 初始化与生成动态项目

    /// <summary>
    /// 初始化顶层 Document 编辑器
    /// </summary>
    /// <param name="document">要编辑的字典</param>
    /// <param name="schemaData">元数据定义</param>
    /// <param name="onSaveAsync">保存回调，返回是否允许保存（用于逻辑校验校验，如 ID 查重）</param>
    /// <param name="contextPath">外部传入的路径描述（可选）</param>
    public void LoadDocumentMetadata(BsonDocument document, SchemaData schemaData, Func<BsonDocument, Task<bool>> onSaveAsync, string? contextPath = null)
    {
        _targetBsonDocument = document;
        _schemaData = schemaData;
        _onSaveAsync = onSaveAsync;
        ContextPath = contextPath;

        Title = string.IsNullOrEmpty(ContextPath)
            ? $"编辑文档 - {schemaData.TargetName}"
            : $"编辑: {ContextPath}";
        Properties.Clear();

        // 依据 Schema 定义构建第一层属性项
        foreach (var propertySchema in schemaData.Properties)
        {
            var itemVm = new DynamicPropertyItemViewModel();
            // 在这一步将会把 document 注入到 ViewModel 里进行监控管理
            itemVm.InitializeWithDocument(_targetBsonDocument, propertySchema.Name, propertySchema);
            Properties.Add(itemVm);
        }

        // 把没有在 Schema 里面定义出来的遗留字段也附加上，类型当做 String
        foreach (var key in _targetBsonDocument.Keys)
        {
            if (!schemaData.Properties.Exists(p => p.Name == key))
            {
                var fallbackSchema = new SchemaProperty
                {
                    Name = key,
                    DisplayName = key,
                    TypeName = "String"
                };
                var itemVm = new DynamicPropertyItemViewModel();
                itemVm.InitializeWithDocument(_targetBsonDocument, key, fallbackSchema);
                Properties.Add(itemVm);
            }
        }
    }

    #endregion

    #region 外部访问器

    /// <summary>
    /// 返回当前正在编辑的 BsonDocument，供洗窗后台代码使用。
    /// </summary>
    public BsonDocument? GetTargetDocument() => _targetBsonDocument;

    #endregion

    #region 保存动作

    /// <summary>
    /// 执行保存逻辑，并返回外部回调的执行结果。
    /// </summary>
    public async Task<bool> ExecuteSaveAsync()
    {
        ClearErrors();
        
        // 执行递归校验
        foreach (var prop in Properties)
        {
            if (!prop.Validate(out string? error))
            {
                WindowErrorMessage = $"保存失败：字段 '{prop.DisplayName}' 或其子项存在错误：{error}";
                return false;
            }
        }

        if (_targetBsonDocument != null && _onSaveAsync != null)
        {
            return await _onSaveAsync.Invoke(_targetBsonDocument);
        }
        return true;
    }

    #endregion

    public void ClearErrors()
    {
        WindowErrorMessage = null;
        foreach (var prop in Properties)
        {
            prop.ErrorMessage = null;
        }
    }
}
