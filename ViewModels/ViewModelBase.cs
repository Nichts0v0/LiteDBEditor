using CommunityToolkit.Mvvm.ComponentModel;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类，继承自 CommunityToolkit.Mvvm 的 ObservableObject，
/// 提供了基础的属性变更通知（INotifyPropertyChanged）支持。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
