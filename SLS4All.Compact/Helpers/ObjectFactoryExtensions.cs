// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Helpers
{
    public static class ObjectFactoryExtensions
    {
        public static ObjectFactoryDisposable<TInstance, TState> CreateDisposable<TInstance, TState>(this IObjectFactory<TInstance, TState> factory, TState? state = default)
            where TInstance : class
            => new ObjectFactoryDisposable<TInstance, TState>(factory, state);

        public static async ValueTask CreateAndCall<TInstance, TState>(this IObjectFactory<TInstance, TState> factory, Func<TInstance, ValueTask> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                await call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }

        public static async Task CreateAndCall<TInstance, TState>(this IObjectFactory<TInstance, TState> factory, Func<TInstance, Task> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                await call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }

        public static async ValueTask<TResult> CreateAndCall<TInstance, TState, TResult>(this IObjectFactory<TInstance, TState> factory, Func<TInstance, ValueTask<TResult>> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                return await call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }

        public static async Task<TResult> CreateAndCall<TInstance, TState, TResult>(this IObjectFactory<TInstance, TState> factory, Func<TInstance, Task<TResult>> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                return await call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }

        public static TResult CreateAndCall<TInstance, TState, TResult>(this IObjectFactory<TInstance, TState> factory, Func<TInstance, TResult> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                return call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }

        public static void CreateAndCall<TInstance, TState>(this IObjectFactory<TInstance, TState> factory, Action<TInstance> call, TState? state = default)
            where TInstance : class
        {
            var instance = factory.CreateObject(state);
            try
            {
                call(instance);
            }
            finally
            {
                factory.DestroyObject(instance);
            }
        }
    }
}
