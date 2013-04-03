using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Reflection;

namespace KIARA
{
  class ObjectAccessor
  {
    #region Public interface

    // Supports empty path in which case modifies passed obj as it's passed by reference.
    static internal void SetValueAtPath(ref object obj, List<PathEntry> path, object value)
    {
      if (path.Count == 0)
      {
        obj = value;
        return;
      }

      // List of value types (structs) to be reassigned.
      List<KeyValuePair<object, PathEntry>> valueTypeContainers = new List<KeyValuePair<object,PathEntry>>();

      object container = obj;
      for (int i = 0; i < path.Count - 1; i++)
      {
        object newContainer = GetMember(container, path[i]);

        // Keep the trail of the value types (struct) or clear it if next container is non-value type.
        if (newContainer.GetType().IsValueType)
          valueTypeContainers.Add(new KeyValuePair<object, PathEntry>(container, path[i]));
        else
          valueTypeContainers.Clear();

        container = newContainer;
      }

      SetMember(container, path[path.Count - 1], value);

      // Reassign the value types (structs).
      for (int i = valueTypeContainers.Count - 1; i >= 0; i--)
      {
        object valueContainer = valueTypeContainers[i].Key;
        PathEntry pathEntry = valueTypeContainers[i].Value;
        SetMember(valueContainer, pathEntry, container);
        container = valueContainer;
      }
    }

    static internal object GetValueAtPath(object obj, List<PathEntry> path)
    {
      for (int i = 0; i < path.Count; i++)
        obj = GetMember(obj, path[i]);
      return obj;
    }

    static internal Type GetTypeAtPath(object obj, List<PathEntry> path)
    {
      for (int i = 0; i < path.Count - 1; i++)
        obj = GetMember(obj, path[i]);
      return GetMemberType(obj, path[path.Count - 1]);
    }

    static internal Type GetElementType(Type currentType)
    {
      if (!typeof(IList).IsAssignableFrom(currentType))
        throw new IncompatibleNativeTypeException();
      if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
        return currentType.GetGenericArguments()[0];
      else if (currentType.IsArray && currentType.HasElementType)
        return currentType.GetElementType();
      return null;
    }

    static internal object GetMember(object obj, PathEntry member) 
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        return ((IList)obj)[member.Index];
      else
        return GetFieldOrPropertyValue(obj, member.Name);
    }

    static internal Type GetMemberType(object obj, PathEntry member)
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        return GetElementType(obj.GetType());
      else
        return GetFieldOrPropertyType(obj.GetType(), member.Name);
    }

    static internal void SetMember(object obj, PathEntry member, object value)
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        ((IList)obj)[member.Index] = value;
      else
        SetFieldOrPropertyValue(obj, member.Name, value);
    }

    static internal object GetFieldOrPropertyValue(object obj, string name)
    {
      FieldInfo fieldInfo = obj.GetType().GetField(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      PropertyInfo propertyInfo = obj.GetType().GetProperty(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      if (fieldInfo != null)
        return fieldInfo.GetValue(obj);
      else if (propertyInfo != null)
        return propertyInfo.GetValue(obj, null);
      throw new IncompatibleNativeTypeException();
    }

    static internal void SetFieldOrPropertyValue(object obj, string name, object value)
    {
      FieldInfo fieldInfo = obj.GetType().GetField(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      PropertyInfo propertyInfo = obj.GetType().GetProperty(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      if (fieldInfo != null)
        fieldInfo.SetValue(obj, value);
      else if (propertyInfo != null)
        propertyInfo.SetValue(obj, value, null);
    }

    static internal Type GetFieldOrPropertyType(Type type, string name)
    {
      FieldInfo fieldInfo = type.GetField(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      PropertyInfo propertyInfo = type.GetProperty(name, BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
      if (fieldInfo != null)
        return fieldInfo.FieldType;
      else if (propertyInfo != null)
        return propertyInfo.PropertyType;
      return null;
    }

    #endregion
  }
}

