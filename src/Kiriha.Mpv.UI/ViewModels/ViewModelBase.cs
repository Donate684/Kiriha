using System;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Mpv.UI.ViewModels;

public abstract class ViewModelBase : ObservableObject
{

    protected bool IsDesignMode => Design.IsDesignMode;

    public virtual void OnNavigatedTo() { }

    public virtual void OnNavigatedFrom() { }
}
