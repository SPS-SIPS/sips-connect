using System.Reflection;
using IPS.Domain.Models.DTOs;

namespace SIPS.Example.Consumer.Enums;

public abstract class Enumeration<TId> : IComparable where TId : IEquatable<TId>
{
    protected Enumeration()
    {
        Id = default!;
        Name = string.Empty;
    }

    protected Enumeration(TId id, string name)
        => (Id, Name) = (id, name);

    public string Name { get; private set; }
    public TId Id { get; private set; }

    public override string ToString() => Name;

    public Lookup<TId> ToLookup => new(Id, Name);

    public static IEnumerable<T> GetAll<T>() where T : Enumeration<TId>
    {
        return typeof(T).GetFields(BindingFlags.Public |
                                   BindingFlags.Static |
                                   BindingFlags.DeclaredOnly)
                        .Select(f => f.GetValue(null))
                        .Cast<T>();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Enumeration<TId> otherValue)
        {
            return false;
        }

        var typeMatches = GetType().Equals(obj.GetType());
        var valueMatches = Id.Equals(otherValue.Id);

        return typeMatches && valueMatches;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static int AbsoluteDifference(Enumeration<TId> firstValue, Enumeration<TId> secondValue)
    {
        var absoluteDifference = Math.Abs(firstValue.Id.GetHashCode() - secondValue.Id.GetHashCode());
        return absoluteDifference;
    }

    public static T FromId<T>(TId id) where T : Enumeration<TId>, new()
    {
        var matchingItem = parse<T, TId>(id, "id", item => item.Id.Equals(id));
        return matchingItem;
    }

    public static T FromName<T>(string name) where T : Enumeration<TId>, new()
    {
        var matchingItem = parse<T, string>(name, "name", item => item.Name == name);
        return matchingItem;
    }

    private static T parse<T, K>(K id, string name, Func<T, bool> predicate) where T : Enumeration<TId>, new()
    {
        var matchingItem = GetAll<T>().FirstOrDefault(predicate);

        if (matchingItem == null)
        {
            var message = string.Format("'{0}' is not a valid {1} in {2}", id, name, typeof(T));
            throw new ApplicationException(message);
        }

        return matchingItem;
    }

    public int CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is not Enumeration<TId> otherValue)
        {
            throw new ArgumentException("Object is not an Enumeration");
        }

        return Id.GetHashCode().CompareTo(otherValue.Id.GetHashCode());
    }
}
