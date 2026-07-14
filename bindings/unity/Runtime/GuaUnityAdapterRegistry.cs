using System.Collections.Generic;
using Gua.Core;
using UnityEngine;

namespace Gua.Unity
{
public interface IGuaUnityControlAdapter
{
    bool TryDescribe(Transform transform, out object target, out string role, out string label, out string value);
    bool TryApply(object target, GuaActionRequest request, out string value);
}

public static class GuaUnityAdapterRegistry
{
    private static readonly List<IGuaUnityControlAdapter> Adapters = new List<IGuaUnityControlAdapter>();

    public static void Register(IGuaUnityControlAdapter adapter)
    {
        if (adapter == null) throw new System.ArgumentNullException(nameof(adapter));
        if (!Adapters.Exists(existing => existing.GetType() == adapter.GetType())) Adapters.Add(adapter);
    }

    internal static bool TryDescribe(Transform transform, out object target, out string role, out string label, out string value)
    {
        foreach (var adapter in Adapters)
            if (adapter.TryDescribe(transform, out target, out role, out label, out value)) return true;
        target = transform.gameObject; role = "panel"; label = transform.name; value = null; return false;
    }

    internal static bool TryApply(object target, GuaActionRequest request, out string value)
    {
        foreach (var adapter in Adapters)
            if (adapter.TryApply(target, request, out value)) return true;
        value = null; return false;
    }
}
}
