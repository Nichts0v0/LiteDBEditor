using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.Services;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 动态属性编辑器窗口的 ViewModel，负责生成、管理和校验基于 Schema 定义的动态编辑项。
/// 适用于编辑 BsonDocument 这种无固定结构的动态对象。
/// </summary>
public partial class DynamicPropertiesViewModel : ViewModelBase
{
    #region 弹窗状态与集合
    
    /// <summary>
    /// 当发生校验错误时触发，请求 UI 自动滚动到指定的错误项位置。
    /// </summary>
    public event Action<DynamicPropertyItemViewModel>? RequestScrollToError;

    /// <summary>
    /// 获取或设置窗口标题。
    /// </summary>
    [ObservableProperty]
    private string _title = LanguageService.GetString("L_EditDocument");

    /// <summary>
    /// 当前正在编辑的动态属性项集合。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DynamicPropertyItemViewModel> _properties = new();

    /// <summary>
    /// 获取或设置显示在窗口底部的全局错误消息。
    /// </summary>
    [ObservableProperty]
    private string? _windowErrorMessage;

    /// <summary>
    /// 获取或设置当前的上下文路径（如：Root.UserInfo），用于在标题中显示层级信息。
    /// </summary>
    [ObservableProperty]
    private string? _contextPath;

    private BsonDocument? _targetBsonDocument;
    private Func<BsonDocument, Task<bool>>? _onSaveAsync;
    private SchemaData? _schemaData;

    /// <summary>
    /// 注入的全局查重回调，用于在编辑 ID 等唯一字段时进行实时校验。
    /// 签名：(字段名, BsonValue) => 是否冲突
    /// </summary>
    public Func<string, BsonValue, bool>? GlobalIdDuplicateCheckFunc { get; set; }

    #endregion

    #region 初始化与生成动态项目

    /// <summary>
    /// 初始化顶层 BsonDocument 编辑器。
    /// </summary>
    /// <param name="document">要编辑的 BsonDocument 实例</param>
    /// <param name="schemaData">元数据定义，决定了如何渲染编辑项</param>
    /// <param name="onSaveAsync">保存回调，允许外部逻辑执行最后的校验（如数据库层面的 ID 查重）</param>
    /// <param name="contextPath">当前的路径上下文，用于 UI 显示</param>
    public void LoadDocumentMetadata(BsonDocument document, SchemaData schemaData, Func<BsonDocument, Task<bool>> onSaveAsync, string? contextPath = null)
    {
        _targetBsonDocument = document;
        _schemaData = schemaData;
        _onSaveAsync = onSaveAsync;
        ContextPath = contextPath;

        UpdateTitle();
        
        // 订阅语言变更以实时更新标题
        LanguageService.LanguageChanged += UpdateTitle;

        Properties.Clear();

        // 第一步：依据 Schema 定义构建预定义的属性项
        foreach (var propertySchema in schemaData.Properties)
        {
            var itemVm = new DynamicPropertyItemViewModel();
            itemVm.IdDuplicateCheckFunc = GlobalIdDuplicateCheckFunc;
            // 将底层 document 的特定字段绑定到该项 ViewModel 进行监控
            itemVm.InitializeWithDocument(_targetBsonDocument, propertySchema.Name, propertySchema);
            Properties.Add(itemVm);
        }

        // 第二步：处理遗漏字段（即在 document 中存在但未在 Schema 中定义的字段）
        foreach (var key in _targetBsonDocument.Keys)
        {
            if (!schemaData.Properties.Exists(p => p.Name == key))
            {
                var fallbackSchema = new SchemaProperty
                {
                    Name = key,
                    DisplayName = key,
                    TypeName = "String" // 未知字段默认降级为字符串编辑
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
    /// 获取当前正在编辑的原始 BsonDocument 实例。
    /// </summary>
    public BsonDocument? GetTargetDocument() => _targetBsonDocument;

    #endregion

    #region 保存动作

    /// <summary>
    /// 执行全量数据校验并尝试调用保存回调。
    /// </summary>
    /// <returns>保存是否成功并允许关闭窗口</returns>
    public async Task<bool> ExecuteSaveAsync()
    {
        WindowErrorMessage = null;

        var errorNames = new System.Collections.Generic.List<string>();
        DynamicPropertyItemViewModel? firstErrorVm = null;

        // 递归收集所有层级的属性校验错误
        foreach (var prop in Properties)
        {
            var vm = prop.CollectAllErrors(errorNames);
            if (firstErrorVm == null && vm != null)
            {
                firstErrorVm = vm;
            }
        }

        // 如果存在校验失败项，则在底部显示提示并尝试滚动到错误位置
        if (errorNames.Count > 0)
        {
            WindowErrorMessage = $"{LanguageService.GetString("L_ValidationErrorPrefix")}{string.Join(", ", errorNames)}";
            
            if (firstErrorVm != null)
            {
                RequestScrollToError?.Invoke(firstErrorVm);
            }
            return false;
        }

        // 触发外部传入的最终保存逻辑
        if (_targetBsonDocument != null && _onSaveAsync != null)
        {
            return await _onSaveAsync.Invoke(_targetBsonDocument);
        }
        return true;
    }

    #endregion

    /// <summary>
    /// 根据当前语言和上下文更新窗口标题。
    /// </summary>
    private void UpdateTitle()
    {
        if (_schemaData == null)
        {
            Title = LanguageService.GetString("L_EditDocument");
            return;
        }

        Title = string.IsNullOrEmpty(ContextPath)
            ? $"{LanguageService.GetString("L_EditDocument")} - {_schemaData.TargetName}"
            : $"{LanguageService.GetString("L_EditData")}: {ContextPath}";
    }

    /// <summary>
    /// 清除所有编辑项的错误状态。
    /// </summary>
    public void ClearErrors()
    {
        WindowErrorMessage = null;
        foreach (var prop in Properties)
        {
            prop.ErrorMessage = null;
        }
    }
}
