// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Helpers
{
    public sealed class ScopedServiceProviderObjectFactory<T, TState> : IObjectFactory<T, TState>
        where T : class
    {
        private readonly static ConditionalWeakTable<T, ConcurrentStack<IServiceScope>> _scopes = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<IServiceProvider, TState, object>? _factoryFunc;

        public ScopedServiceProviderObjectFactory(
            IServiceProvider serviceProvider,
            Func<IServiceProvider, TState, object>? factoryFunc)
        {
            _serviceProvider = serviceProvider;
            _factoryFunc = factoryFunc;
        }

        object IObjectFactory.CreateObject(object? state)
            => CreateObject((TState?)state);

        public T CreateObject(TState? state = default)
        {
            var scope = _serviceProvider.CreateScope();
            T obj;
            if (state == null)
                obj = scope.ServiceProvider.GetRequiredService<T>();
            else
            {
                if (_factoryFunc == null)
                    throw new InvalidOperationException("Cannot create object with specified state, factoryFunc was not specified in constructor");
                obj = (T)_factoryFunc(scope.ServiceProvider, state);
            }
            var bag = _scopes.GetOrCreateValue(obj);
            bag.Push(scope);
            return obj;
        }

        public void DestroyObject(object? obj)
        {
            var value = obj as T;
            if (value != null && _scopes.TryGetValue(value, out var bag) && bag.TryPop(out var scope))
            {
                scope.Dispose();
            }
        }
    }

    public static class ScopedServiceProviderObjectFactory
    {
        public static IObjectFactory Create(IServiceProvider serviceProvider, Type type)
        {
            var impl = typeof(ScopedServiceProviderObjectFactory<,>).MakeGenericType(type, typeof(object));
            return (IObjectFactory)Activator.CreateInstance(impl, serviceProvider, null)!;
        }

        public static IObjectFactory Create<T, TState>(IServiceProvider serviceProvider, Func<IServiceProvider, TState, T> factory)
            where T: class
        {
            return new ScopedServiceProviderObjectFactory<T, TState>(serviceProvider, factory);
        }
    }
}
