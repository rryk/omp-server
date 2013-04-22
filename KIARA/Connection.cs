﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KIARA
{
    public delegate FunctionCall FunctionWrapper(params object[] parameters);

    public partial class Connection
    {
        #region Public interface
        // Loads an IDL file from the |uri|. Parses it's content and adds new types and services to 
        // the type system. When called on a |uri| that was already loaded, does not raise an error.
        public void LoadIDL(string uri)
        {
            Implementation.LoadIDL(uri);
        }

        // Returns a function wrapper for an IDL method with |qualifiedMethodName| that sends a call
        // to the remote end using |typeMapping| for serialization/desirialization.
        public FunctionWrapper GenerateFunctionWrapper(string qualifiedMethodName, 
                                                    string typeMapping) {
            return GenerateFunctionWrapper(qualifiedMethodName, typeMapping, 
                                           new Dictionary<string, Delegate>());
        }

        // Same as above, but |defaultHandlers| are automatically assigned to each call.
        public FunctionWrapper GenerateFunctionWrapper(
            string qualifiedMethodName, string typeMapping, 
            Dictionary<string, Delegate> defaultHandlers)
        {
            return Implementation.GenerateFuncWrapper(qualifiedMethodName, typeMapping, 
                                                      defaultHandlers);
        }

        // Registers nativeMethod as an implementation of the IDL method with qualifiedMethodName. 
        // Parameters, return value and exceptions are serialized/deserialized according to 
        // typeMapping. When called more than once on the same |qualifiedMethodName| will
        // override previous entry and use |nativeMethod| that was passed with the last call.
        // To pass an arbitrary method in place of the |nativeMethod| argument the user must and 
        // cast passed method to a respective delegate type implicitly. For example, if a user needs
        // to use a static method Bar of the class Foo as an implementation for some IDL function 
        // "myservice.foobar", he would need to write the following code:
        //
        //   class Foo {
        //     public static int Bar(float x, string s) { ... }
        //   };
        //
        //   delegate int FooBarDelegate(float x, string s);
        //
        //   connection.RegisterFuncImplementation("myservice.foobar", "...", 
        //                                         (FooBarDelegate)Foo.Bar);
        //
        // One can also use Func template that is available in .NET Framework 3.5 or later to avoid 
        // declaring a new delegate type for each registered function:
        //
        //   connection.RegisterFuncImplementation("myservice.foobar", "...", 
        //                                         (Func<float, string, int>)Foo.Bar);
        //
        // It is possible to pass static/instance, private/public methods, delegates or lambda 
        // functions, but all of them must be implicity casted to some delegate type as shown
        // above.
        public void RegisterFuncImplementation(string qualifiedMethodName, string typeMapping, 
                                               Delegate nativeMethod)
        {
            Implementation.RegisterFuncImplementation(qualifiedMethodName, typeMapping, 
                                                      nativeMethod);
        }
        #endregion

        #region Private implementation
        internal interface IImplementation
        {
            void LoadIDL(string uri);
            FunctionWrapper GenerateFuncWrapper(string qualifiedMethodName, string typeMapping,
                                                Dictionary<string, Delegate> defaultHandlers);
            void RegisterFuncImplementation(string qualifiedMethodName, string typeMapping, 
                                            Delegate nativeMethod);
        }

        private IImplementation Implementation;
        #endregion
  }
}
