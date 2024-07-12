// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using SLS4All.Compact.ComponentModel;

namespace SLS4All.Compact.Scripts
{
    public interface IJSFieldValue
    {
        bool HasValue { get; }
        object? Value { get; }
    }

    public readonly record struct JSFieldValue<T>(bool HasValue, T Value) : IJSFieldValue
    {
        object? IJSFieldValue.Value => Value;

        public static implicit operator JSFieldValue<T>(T value)
            => new JSFieldValue<T>(true, value);
    }
    
    [AttributeUsage(AttributeTargets.Method)]
    public class JSFieldAttribute : Attribute
    {
        public string Name { get; }

        public JSFieldAttribute(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class JSMethodAttribute : Attribute
    {
        public string Name { get; }

        public JSMethodAttribute(string name)
        {
            Name = name;
        }
    }
    public interface IJSProxy : IAsyncDisposable
    {
        IJSRuntime Runtime { get; }
        IJSObjectReference Reference { get; }

        ValueTask IAsyncDisposable.DisposeAsync()
            => Reference.DisposeAsync();
    }

    public static class JSProxy
    {
        [DebuggerDisplay("JSObjectReferenceProxy")] // helps debugger crashes
        public class JSObjectReferenceProxy : DispatchProxy
        {
            private static readonly MethodInfo _invokeProxyInner;
            private static readonly MethodInfo _invokeProxyTaskInner;
            private static readonly MethodInfo _invokeInner;
            private static readonly MethodInfo _invokeTaskInner;
            internal IJSRuntime _runtime;
            internal IJSObjectReference _reference;

            static JSObjectReferenceProxy()
            {
                _invokeProxyInner = typeof(JSObjectReferenceProxy).GetMethod(nameof(InvokeProxyInner), BindingFlags.NonPublic | BindingFlags.Static)!;
                _invokeProxyTaskInner = typeof(JSObjectReferenceProxy).GetMethod(nameof(InvokeProxyTaskInner), BindingFlags.NonPublic | BindingFlags.Static)!;
                _invokeInner = typeof(JSObjectReferenceProxy).GetMethod(nameof(InvokeInner), BindingFlags.NonPublic | BindingFlags.Static)!;
                _invokeTaskInner = typeof(JSObjectReferenceProxy).GetMethod(nameof(InvokeTaskInner), BindingFlags.NonPublic | BindingFlags.Static)!;
            }

            [Obsolete("Ctor should not be called directly")]
            public JSObjectReferenceProxy()
            {
                _runtime = default!;
                _reference = default!;
            }

            private static async ValueTask<T> InvokeProxyInnerTyped<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
            {
                if (target is IJSObjectReference reference)
                {
                    var result = await reference.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    return CreateProxy<T>(runtime, result);
                }
                else
                {
                    var result = await runtime.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    return CreateProxy<T>(runtime, result);
                }
            }

            static async ValueTask<T> InvokeInnerTyped<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
            {
                JsonDocument doc;
                var type = typeof(T);
                if (typeof(IJSProxy).IsAssignableFrom(type))
                {
                    IJSObjectReference res;
                    if (target is IJSObjectReference reference)
                        res = await reference.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    else
                        res = await runtime.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    return res != null ? (T)CreateProxy(type, runtime, res) : default(T)!;
                }
                else if (type.IsArray && typeof(IJSProxy).IsAssignableFrom(type.GetElementType()))
                {
                    // .NET does not currently support returning IJSObjectReference array directly (IJSObjectReference[])
                    //IJSObjectReference[] res;
                    //if (target is IJSObjectReference reference)
                    //    res = await reference.InvokeAsync<IJSObjectReference[]>(name, cancel, args);
                    //else
                    //    res = await runtime.InvokeAsync<IJSObjectReference[]>(name, cancel, args);
                    //var elementType = type.GetElementType()!;
                    //return (T)(object)res.Select(x => x != null ? CreateProxy(elementType, runtime, x) : null!).ToArray();

                    IJSObjectReference jsArray;
                    if (target is IJSObjectReference reference)
                        jsArray = await reference.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    else
                        jsArray = await runtime.InvokeAsync<IJSObjectReference>(name, cancel, args);
                    if (jsArray == null)
                        return default!;
                    else
                    {
                        var length = await runtime.InvokeAsync<int>("AppHelpersInvoke", cancel, "getLength", jsArray);
                        var elementType = type.GetElementType()!;
                        var res = Array.CreateInstance(elementType, length);
                        await Parallel.ForEachAsync(
                            Enumerable.Range(0, res.Length),
                            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancel },
                            async (i, cancel) =>
                            {
                                var item = await runtime.InvokeAsync<IJSObjectReference>("AppHelpersInvoke", cancel, "getElement", jsArray, i);
                                res.SetValue(item != null ? CreateProxy(elementType, runtime, item) : null!, i);
                            });
                        return (T)(object)res;
                    }
                }
                else
                {
                    if (target is IJSObjectReference reference)
                        doc = await reference.InvokeAsync<JsonDocument>(name, cancel, args);
                    else
                        doc = await runtime.InvokeAsync<JsonDocument>(name, cancel, args);
                    return JSConverter.ToNet<T>(doc);
                }
            }

            private static object InvokeProxyInner<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
                => InvokeProxyInnerTyped<T>(runtime, target, name, cancel, args);

            private static object InvokeInner<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
                => InvokeInnerTyped<T>(runtime, target, name, cancel, args);

            private static Task<T> InvokeProxyTaskInner<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
                where T : IJSProxy, new()
                => InvokeProxyInnerTyped<T>(runtime, target, name, cancel, args).AsTask();

            private static Task<T> InvokeTaskInner<T>(IJSRuntime runtime, object target, string name, CancellationToken cancel, object[] args)
                => InvokeInnerTyped<T>(runtime, target, name, cancel, args).AsTask();

            private static ValueTask InvokeInnerVoid(object target, string name, CancellationToken cancel, object?[]? args)
            {
                if (target is IJSObjectReference reference)
                {
                    return reference.InvokeVoidAsync(name, cancel, args);
                }
                else
                {
                    return ((IJSRuntime)target).InvokeVoidAsync(name, cancel, args!);
                }
            }

            protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod == null)
                    throw new ArgumentNullException(nameof(targetMethod));
                if (targetMethod.Name == $"get_{nameof(IJSProxy.Reference)}")
                    return _reference;
                else if (targetMethod.Name == $"get_{nameof(IJSProxy.Runtime)}")
                    return _runtime;
                else if (targetMethod.Name == nameof(IJSProxy.DisposeAsync))
                    return _reference.DisposeAsync();
                else
                {
                    var prop = targetMethod.GetCustomAttribute<JSFieldAttribute>();
                    if (prop != null)
                    {
                        if (args == null || args.Length == 0) // getter
                            return InvokeProxyAsync(_runtime, targetMethod.ReturnType, _runtime, "AppHelpersGet", _reference, prop.Name);
                        if (args?.Length != 1 || args[0] is not IJSFieldValue fieldValue)
                            throw new ArgumentException("Field getters/setters must have single argument of IJSFieldValue");
                        if (fieldValue.HasValue) // setter
                            return InvokeProxyAsync(_runtime, targetMethod.ReturnType, _runtime, "AppHelpersSet", _reference, prop.Name, fieldValue.Value);
                        else // getter
                            return InvokeProxyAsync(_runtime, targetMethod.ReturnType, _runtime, "AppHelpersGet", _reference, prop.Name);
                    }
                    else
                    {
                        var method = targetMethod.GetCustomAttribute<JSMethodAttribute>();
                        var args2 = new object[(args?.Length ?? 0) + 2];
                        if (args != null)
                            args.CopyTo(args2, 2);
                        args2[0] = _reference;
                        args2[1] = method?.Name ?? targetMethod.Name;
                        return InvokeProxyAsync(_runtime, targetMethod.ReturnType, _runtime, "AppHelpersInvokeOnTarget", args2);
                    }
                }
            }

            public static object InvokeProxyAsync(IJSRuntime runtime, Type resultType, object target, string name, params object?[]? args)
            {
                var cancel = CancellationToken.None;
                if (args?.Length > 0 && args[^1] is CancellationToken lastArgToken)
                {
                    Array.Resize(ref args, args.Length - 1);
                    cancel = lastArgToken;
                }
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        args[i] = JSConverter.ToTS(args[i]);
                    }
                }

                if (resultType == typeof(ValueTask))
                    return InvokeInnerVoid(target, name, cancel, args);
                else if (resultType == typeof(Task))
                    return InvokeInnerVoid(target, name, cancel, args).AsTask();
                else if (resultType.IsGenericType)
                {
                    var func = CreateInvokeFunc(resultType);
                    return func(runtime, target, name, cancel, args ?? Array.Empty<object>());
                }
                else if (resultType == null || resultType == typeof(void))
                    return InvokeInnerVoid(target, name, cancel, args);
                else
                    throw new InvalidOperationException("Invalid non-generic result type");
            }

            private static Func<IJSRuntime, object, string, CancellationToken, object?[], object> CreateInvokeFunc(Type resultType)
            {
                if (!_invokeCache.TryGetValue(resultType, out var func))
                {
                    MethodInfo method;
                    if (resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                    {
                        var arg = resultType.GetGenericArguments()[0];
                        if (typeof(IJSProxy).IsAssignableFrom(arg))
                            method = _invokeProxyInner.MakeGenericMethod(arg);
                        else
                            method = _invokeInner.MakeGenericMethod(arg);
                    }
                    else if (resultType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var arg = resultType.GetGenericArguments()[0];
                        if (typeof(IJSProxy).IsAssignableFrom(arg))
                            method = _invokeProxyTaskInner.MakeGenericMethod(arg);
                        else
                            method = _invokeTaskInner.MakeGenericMethod(arg);
                    }
                    else
                        throw new InvalidOperationException("Invalid generic result type");
                    func = (Func<IJSRuntime, object, string, CancellationToken, object?[], object>)Delegate.CreateDelegate(
                        typeof(Func<IJSRuntime, object, string, CancellationToken, object?[], object>),
                        method);
                    if (!_invokeCache.TryAdd(resultType, func))
                        func = _invokeCache[resultType];
                }
                return func;
            }
        }

        private readonly static ConcurrentDictionary<Type, Func<IJSRuntime, IJSObjectReference, IJSProxy>> _createProxyCache = new();
        private readonly static ConcurrentDictionary<Type, Func<IJSRuntime, object, string, CancellationToken, object?[], object>> _invokeCache = new();

        public static T CreateProxy<T>(IJSRuntime runtime, IJSObjectReference reference)
        {
            var proxy = DispatchProxy.Create<T, JSObjectReferenceProxy>()!;
            var t = (JSObjectReferenceProxy)(object)proxy;
            t._runtime = runtime;
            t._reference = reference;
            return proxy;
        }

        public static IJSProxy CreateProxy(Type proxyType, IJSRuntime runtime, IJSObjectReference reference)
        {
            if (!_createProxyCache.TryGetValue(proxyType, out var func))
            {
                var method = typeof(JSProxy).GetMethods(BindingFlags.Static | BindingFlags.Public).Single(x => x.Name == nameof(CreateProxy) && x.IsGenericMethod);
                var method2 = method.MakeGenericMethod(proxyType);
                func = (Func<IJSRuntime, IJSObjectReference, IJSProxy>)Delegate.CreateDelegate(typeof(Func<IJSRuntime, IJSObjectReference, IJSProxy>), method2);
                if (!_createProxyCache.TryAdd(proxyType, func))
                    func = _createProxyCache[proxyType];
            }
            return func(runtime, reference);
        }

        public static ValueTask<T> InvokeProxyAsync<T>(this IJSRuntime runtime, string name, params object?[] args)
            => (ValueTask<T>)JSObjectReferenceProxy.InvokeProxyAsync(runtime, typeof(ValueTask<T>), runtime, name, args);

        public static ValueTask<T> InvokeProxyAsync<T>(this IJSRuntime runtime, IJSObjectReference target, string name, params object?[] args)
            => (ValueTask<T>)JSObjectReferenceProxy.InvokeProxyAsync(runtime, typeof(ValueTask<T>), target, name, args);
    }
}
