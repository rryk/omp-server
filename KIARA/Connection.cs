using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
  public delegate FunctionCall FuncWrapper(params object[] parameters);

  public partial class Connection
  {
    #region Public interface
    // Returns a function wrapper for an IDL method with qualifiedMethodName, send a call to the remove end and 
    // assign defaultHandlers if any (see FunctionCall for handler types). Parameters, exception and return value
    // are serialized/deserialized according to typeMapping.
    public FuncWrapper GenerateFuncWrapper(string qualifiedMethodName, string typeMapping, params object[] defaultHandlers)
    {
      return Implementation.GenerateFuncWrapper(qualifiedMethodName, typeMapping, defaultHandlers);
    }

    // Registers nativeMethod as an implementation of the IDL method with qualifiedMethodName. Parameters,
    // return value and exceptions are serialized/deserialized according to typeMapping. To pass an arbitrary method
    // in place of the nativeMethod argument the user must declarare respective delegate type and cast passed method
    // implicitly. For example, let's say the user wants to use a static method Bar of the class Foo as an 
    // implementation for some IDL function "myservice.foobar":
    //
    //   class Foo {
    //     public static int Bar(float x) {
    //   };
    //
    //   delegate int FooBarDelegate(float x);
    //
    //   ...
    //   connection.RegisterFuncImplementation("myservice.foobar", "...", (FooBarDelegate)Foo.Bar);
    //   ...
    //
    // It is possible to pass static or instance, private or public methods, delegates or lambda functions, but any
    // of them must be implicity casted to some delegate type before being passed to this method.
    public void RegisterFuncImplementation(string qualifiedMethodName, string typeMapping, Delegate nativeMethod)
    {
      Implementation.RegisterFuncImplementation(qualifiedMethodName, typeMapping, nativeMethod);
    }
    #endregion

    #region Private implementation
    internal interface IImplementation
    {
      FuncWrapper GenerateFuncWrapper(string qualifiedMethodName, string typeMapping, params object[] defaultHandlers);
      void RegisterFuncImplementation(string qualifiedMethodName, string typeMapping, Delegate nativeMethod);
    }

    private IImplementation Implementation;
    #endregion
  }
}
