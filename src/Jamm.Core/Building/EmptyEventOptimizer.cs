using Jamm.ModMaker;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

internal sealed record EmptyEventOptimizationResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Skipped);

/// <summary>Removes event entries proven to do no work after the merged archive is assembled.</summary>
internal static class EmptyEventOptimizer
{
    internal static EmptyEventOptimizationResult Apply(UndertaleData data)
    {
        List<string> removed = [];
        List<string> skipped = [];
        foreach (var obj in data.GameObjects)
        {
            for (int eventType = 0; eventType < obj.Events.Count; eventType++)
            {
                var events = obj.Events[eventType];
                foreach (var ev in events.ToArray())
                {
                    if (!IsNoOp(data, ev)) continue;
                    string eventTypeName = Enum.GetName((EventType)eventType) ?? $"Event{eventType}";
                    string name = $"{obj.Name.Content}_{eventTypeName}_{ev.EventSubtype}";
                    if (ParentHasBehavior(data, obj.ParentId, eventType, ev.EventSubtype))
                    {
                        skipped.Add(name);
                        continue;
                    }

                    events.Remove(ev);
                    removed.Add(name);
                }
            }
        }
        return new(removed, skipped);
    }

    private static bool ParentHasBehavior(UndertaleData data, UndertaleGameObject? parent, int eventType, uint subtype)
    {
        HashSet<UndertaleGameObject> seen = [];
        while (parent is not null && seen.Add(parent))
        {
            var inheritedEvent = parent.Events[eventType].FirstOrDefault(candidate => candidate.EventSubtype == subtype);
            if (inheritedEvent is not null)
            {
                if (inheritedEvent.Actions.Count == 0) return false;
                if (IsExplicitInheritedCall(data, inheritedEvent))
                {
                    parent = parent.ParentId;
                    continue;
                }
                return !IsNoOp(data, inheritedEvent);
            }
            parent = parent.ParentId;
        }
        return false;
    }

    private static bool IsNoOp(UndertaleData data, UndertaleGameObject.Event ev)
    {
        if (ev.Actions.Count == 0) return true;
        foreach (var action in ev.Actions)
        {
            if (action.CodeId is not UndertaleCode code) return false;
            if (code.Instructions.Count == 0) continue;
            string source;
            try { source = ModObjects.Decompile(data, code); }
            catch { return false; }
            string normalized = string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
            if (normalized is not ("action_inherited();" or "action_inherited()")) return false;
        }
        return true;
    }

    private static bool IsExplicitInheritedCall(UndertaleData data, UndertaleGameObject.Event ev)
    {
        if (ev.Actions.Count == 0) return false;
        foreach (var action in ev.Actions)
        {
            if (action.CodeId is not UndertaleCode code) return false;
            string source;
            try { source = ModObjects.Decompile(data, code); }
            catch { return false; }
            string normalized = string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
            if (normalized is not ("action_inherited();" or "action_inherited()")) return false;
        }
        return true;
    }
}
