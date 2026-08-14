using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kiriha.Core.Domain.Models;

/// <summary>
/// A lightweight base class implementing INotifyPropertyChanged for domain models.
/// Replaces CommunityToolkit.Mvvm to avoid dragging MVVM dependencies into the domain layer.
/// </summary>
public abstract class DomainObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, newValue))
        {
            return false;
        }

        field = newValue;
        OnPropertyChanged(propertyName);
        return true;
    }
}
