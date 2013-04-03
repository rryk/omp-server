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
    static public void SetValueAtPath(ref object obj, List<PathEntry> path, object value)
    {
      if (path.Count == 0)
      {
        obj = value;
        return;
      }

      object container = obj;
      for (int i = 0; i < path.Count - 1; i++)
        container = GetMember(container, path[i]);
      SetMember(container, path[path.Count - 1], value);
    }

    static public object GetValueAtPath(object obj, List<PathEntry> path)
    {
      for (int i = 0; i < path.Count; i++)
        obj = GetMember(obj, path[i]);
      return obj;
    }

    static public Type GetTypeAtPath(object obj, List<PathEntry> path)
    {
      for (int i = 0; i < path.Count - 1; i++)
        obj = GetMember(obj, path[i]);
      return GetMemberType(obj, path[path.Count - 1]);
    }

    static public Type GetElementType(Type currentType)
    {
      if (!typeof(IList).IsAssignableFrom(currentType))
        throw new IncompatibleNativeTypeException();
      if (currentType.IsGenericType && currentType.GetGenericTypeDefinition() == typeof(List<>))
        return currentType.GetGenericArguments()[0];
      else if (currentType.IsArray && currentType.HasElementType)
        return currentType.GetElementType();
      return null;
    }

    static public object GetMember(object obj, PathEntry member) 
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        return ((IList)obj)[member.Index];
      else
        return GetFieldOrPropertyValue(obj, member.Name);
    }

    static public Type GetMemberType(object obj, PathEntry member)
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        return GetElementType(obj.GetType());
      else
        return GetFieldOrPropertyType(obj.GetType(), member.Name);
    }

    static public void SetMember(object obj, PathEntry member, object value)
    {
      if (member.Kind == PathEntry.PathEntryKind.Index)
        ((IList)obj)[member.Index] = value;
      else
        SetFieldOrPropertyValue(obj, member.Name, value);
    }

    static public object GetFieldOrPropertyValue(object obj, string name)
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

    static public void SetFieldOrPropertyValue(object obj, string name, object value)
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

    public static Type GetFieldOrPropertyType(Type type, string name)
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

