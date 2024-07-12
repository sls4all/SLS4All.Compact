// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Validation
{
    public sealed record class ValidationContext(IServiceProvider ServiceProvider, CancellationToken Cancel, Func<object, object?>? OverrideObj = null)
    {
        private Dictionary<object, object?>? _statesLazy;
        private HashSet<object>? _parentsLazy;

        public HashSet<object> Parents
        {
            get
            {
                if (_parentsLazy == null)
                    _parentsLazy = new HashSet<object>(ReferenceEqualityComparer.Instance);
                return _parentsLazy;
            }
        }

        public object? this[object stateKey]
        {
            get
            {
                if (_statesLazy != null)
                    return _statesLazy.GetValueOrDefault(stateKey);
                else
                    return null;
            }
            set
            {
                _statesLazy ??= new();
                _statesLazy[stateKey] = value;
            }
        }

        public ValidationHelper CreateHelper(object obj)
        {
            if (OverrideObj != null)
                obj = OverrideObj(obj) ?? obj;
            return new ValidationHelper(obj);
        }

        public void SetState<T>(T? value)
            => this[typeof(T)] = value;
    }
}
