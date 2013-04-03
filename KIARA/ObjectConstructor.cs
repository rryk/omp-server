using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections;

namespace KIARA
{
  internal class ObjectConstructor
  {
    #region Public interface

    static public object ConstructObject(List<WireEncoding> encoding, Type objectType)
    {
      Sketch sketch = ConstructSketch(encoding, objectType);
      return ConstructFromSketch(sketch);
    }

    public static IList ConstructAndAllocateArray(Type arrayType, int size)
    {
      if (!typeof(IList).IsAssignableFrom(arrayType))
        throw new IncompatibleNativeTypeException();
      IList array = (IList)ConstructObject(arrayType, size);
      while (array.Count < size)
      {
        object defaultValue = ConstructObject(ObjectAccessor.GetElementType(arrayType));
        array.Add(defaultValue);
      }
      return array;
    }

    #endregion

    #region Private implementation

    class Sketch
    {
      public enum SketchKind
      {
        Undefined,
        Base,
        Array,
        Object
      }
      public SketchKind Kind = SketchKind.Undefined;
      public Type Type;
      public int Size = 0;                       // for Kind == Array
      public Dictionary<int, Sketch> Elements;   // for Kind == Array
      public Dictionary<string, Sketch> Fields;  // for Kind == Object
    }

    static Sketch ConstructSketch(List<WireEncoding> encoding, Type objectType)
    {
      Sketch sketch = new Sketch();
      foreach (WireEncoding encodingEntry in encoding)
      {
        Sketch currentSketch = sketch;
        Type currentType = objectType;
        currentSketch.Type = currentType;
        foreach (PathEntry pathEntry in encodingEntry.ValuePath)
        {
          if (currentSketch.Kind != Sketch.SketchKind.Undefined)
          {
            if (pathEntry.Kind == PathEntry.PathEntryKind.Index && currentSketch.Kind != Sketch.SketchKind.Array)
              throw new IncompatibleNativeTypeException();
            else if (pathEntry.Kind == PathEntry.PathEntryKind.Name && currentSketch.Kind != Sketch.SketchKind.Object)
              throw new IncompatibleNativeTypeException();
          }
          else
          {
            if (pathEntry.Kind == PathEntry.PathEntryKind.Index)
            {
              currentSketch.Kind = Sketch.SketchKind.Array;
              currentSketch.Elements = new Dictionary<int, Sketch>();
            }
            else if (pathEntry.Kind == PathEntry.PathEntryKind.Name)
            {
              currentSketch.Kind = Sketch.SketchKind.Object;
              currentSketch.Fields = new Dictionary<string, Sketch>();
            }
          }

          if (currentSketch.Kind == Sketch.SketchKind.Array)
          {
            if (currentSketch.Size < pathEntry.Index + 1)
              currentSketch.Size = pathEntry.Index + 1;
            currentType = ObjectAccessor.GetElementType(currentType);
            if (currentType == null)  
              throw new IncompatibleNativeTypeException();
            if (!currentSketch.Elements.ContainsKey(pathEntry.Index))
              currentSketch.Elements[pathEntry.Index] = new Sketch();
            currentSketch = currentSketch.Elements[pathEntry.Index];
          }
          else if (currentSketch.Kind == Sketch.SketchKind.Object)
          {
            currentType = ObjectAccessor.GetFieldOrPropertyType(currentType, pathEntry.Name);
            if (currentType == null)
              throw new IncompatibleNativeTypeException();
            if (!currentSketch.Fields.ContainsKey(pathEntry.Name))
              currentSketch.Fields[pathEntry.Name] = new Sketch();
            currentSketch = currentSketch.Fields[pathEntry.Name];
          }

          currentSketch.Type = currentType;
        }
        
        currentSketch.Kind = Sketch.SketchKind.Base;
      }
      return sketch;
    }

    static object ConstructFromSketch(Sketch sketch)
    {
      object obj = null;
      if (sketch.Kind == Sketch.SketchKind.Array)
      {
        IList array = ConstructAndAllocateArray(sketch.Type, sketch.Size);

        foreach (KeyValuePair<int, Sketch> element in sketch.Elements)
          array[element.Key] = ConstructFromSketch(element.Value);

        obj = array;
      }
      else if (sketch.Kind == Sketch.SketchKind.Object)
      {
        obj = ConstructObject(sketch.Type);
        foreach (KeyValuePair<string, Sketch> element in sketch.Fields)
          ObjectAccessor.SetFieldOrPropertyValue(obj, element.Key, ConstructFromSketch(element.Value));
      }
      else if (sketch.Kind == Sketch.SketchKind.Base)
        obj = ConstructObject(sketch.Type);

      return obj;
    }

    private static object ConstructObject(Type type, params object[] constructorParams)
    {
      if (type == typeof(string))
        return null;  // Strings do not have default constructor.
      else if (type.IsArray && constructorParams.Length != 1)
        return null;  // Only construct fixed arrays if size is passed.
      else
        return Activator.CreateInstance(type, constructorParams);
    }

    #endregion
  }
}
