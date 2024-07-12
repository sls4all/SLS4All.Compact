// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Helpers
{
    public sealed class DelegatedObjectFactory<T, TState> : IObjectFactory<T, TState>
        where T: class
    {
        private readonly Func<object?, T> _create;
        private readonly Action<T> _destroy;

        public DelegatedObjectFactory(
            Func<object?, T> create,
            Action<T> destroy)
        {
            _create = create;
            _destroy = destroy;
        }

        public T CreateObject(TState? state = default)
            => _create(state);

        object IObjectFactory.CreateObject(object? state)
            => CreateObject((TState?)state);

        public void DestroyObject(object? obj)
        {
            if (obj is T target)
                _destroy(target);
        }
    }
}
