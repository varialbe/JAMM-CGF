using System.Collections;
using System.Reflection;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Jamm.Core.Building;

// Copies model-owned graphs while translating every table reference into the destination model.
// Private fields preserve UTML's wrapper sentinel values and action metadata, unlike JSON cloning.
internal sealed class ResourceImporter
{
    private readonly UndertaleData target;
    private readonly Dictionary<object, object> mapped = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, IList> payloads = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> named = new(ReferenceEqualityComparer.Instance);
    private static readonly MethodInfo Shallow = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal ResourceImporter(UndertaleData source, UndertaleData target)
    {
        this.target = target;
        var targets = ResourceCatalog.Tables(target);
        foreach (var (family, table) in ResourceCatalog.Tables(source))
            foreach (object value in table)
            {
                named.Add(value);
                if (ResourceCatalog.Find(targets[family], ResourceCatalog.Name(value)) is { } destination) mapped[value] = destination;
            }
        Payload(source.EmbeddedTextures, target.EmbeddedTextures);
        Payload(source.TexturePageItems, target.TexturePageItems);
        Payload(source.EmbeddedAudio, target.EmbeddedAudio);
        foreach (var group in source.AudioGroups ?? [])
        {
            var match = target.AudioGroups?.FirstOrDefault(g => g.Name.Content == group.Name.Content);
            if (match is not null) mapped[group] = match;
            else if (target.AudioGroups is not null) payloads[group] = (IList)target.AudioGroups;
        }
        void Payload<T>(IList<T>? from, IList<T>? to)
        {
            if (from is null || to is null) return;
            foreach (var value in from) payloads[value!] = (IList)to;
        }
    }

    internal object Copy(object source) => Clone(source)!;
    internal void Populate(object source, object destination, bool skipFontTexture = false)
    {
        mapped[source] = destination;
        CopyFields(source, destination, skipFontTexture);
    }

    private object? Clone(object? value)
    {
        if (value is null) return null;
        Type type = value.GetType();
        if (type.IsValueType || value is string) return value;
        if (value is Delegate) return null;
        if (mapped.TryGetValue(value, out var existing)) return existing;
        if (value is UndertaleString text)
        {
            var interned = target.Strings.MakeString(text.Content);
            mapped[value] = interned;
            return interned;
        }
        if (named.Contains(value))
            throw new InvalidDataException($"Missing merged resource '{ResourceCatalog.Name(value)}'. Choose a resolution that retains its dependency.");
        if (value is Array array)
        {
            var copy = (Array)array.Clone(); mapped[value] = copy;
            if (!type.GetElementType()!.IsValueType)
                for (int i = 0; i < array.Length; i++) copy.SetValue(Clone(array.GetValue(i)), i);
            return copy;
        }
        if (value is IList list)
        {
            var copy = (IList)Activator.CreateInstance(type)!; mapped[value] = copy;
            foreach (object? item in list) copy.Add(Clone(item));
            return copy;
        }
        object result = Shallow.Invoke(value, null)!;
        mapped[value] = result;
        CopyFields(value, result);
        if (payloads.TryGetValue(value, out var table)) table.Add(result);
        return result;
    }

    private void CopyFields(object source, object destination, bool skipFontTexture = false)
    {
        for (Type? type = source.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (skipFontTexture && source is UndertaleFont && field.Name == "<Texture>k__BackingField") continue;
                if (source is UndertaleEmbeddedTexture or UndertaleTexturePageItem or UndertaleEmbeddedAudio && field.Name.Contains("<Name>", StringComparison.Ordinal))
                    field.SetValue(destination, new UndertaleString(((UndertaleNamedResource)source).Name?.Content ?? ""));
                else field.SetValue(destination, Clone(field.GetValue(source)));
            }
    }
}
