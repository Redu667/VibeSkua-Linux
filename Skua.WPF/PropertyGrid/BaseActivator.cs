using Skua.Core.Interfaces;
using System;

namespace Skua.WPF;

public class BaseActivator : IActivator
{
    public virtual object CreateInstance(Type type, params object[] args)
    {
        return type == null
            ? throw new ArgumentNullException("type")
            : type == typeof(DynamicObject)
            ? new DynamicObject()
            : type == typeof(PropertyGridProperty)
            ? new PropertyGridProperty((PropertyGridDataProvider)args[0])
            : Activator.CreateInstance(type, args);
    }
}