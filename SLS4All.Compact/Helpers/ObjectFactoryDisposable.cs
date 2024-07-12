// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Helpers
{
    public struct ObjectFactoryDisposable<T, TState> : IDisposable
        where T : class
    {
        private readonly IObjectFactory<T, TState> _factory;
        private T? _instance;

        public T Instance
        {
            get => _instance ?? throw new ObjectDisposedException(null);
        }

        public ObjectFactoryDisposable(IObjectFactory<T, TState> factory, TState? state)
        {
            _factory = factory;
            _instance = _factory.CreateObject(state);
        }

        public void Dispose()
        {
            var instance = _instance;
            if (instance != null)
            {
                _instance = null;
                _factory.DestroyObject(instance);
            }
        }
    }
}
