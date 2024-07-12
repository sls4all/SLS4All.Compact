// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Storage
{
    public interface IStorageObject : IValidatable
    {
        Guid Id { get; set; }

        void MergeFrom(IStorageObject other);

        IStorageObject Clone();
    }

    public static class StorageObjectExtensions
    {
        public static T? CloneAndMergeFrom<T>(this T? originalObject, T? overrideObject)
            where T: class, IStorageObject
        {
            if (originalObject == null)
                return (T?)overrideObject?.Clone();
            else if (overrideObject == null)
                return (T)originalObject.Clone();
            else
            {
                var clone = (T)originalObject.Clone();
                clone.MergeFrom(overrideObject);
                return clone;
            }
        }
    }
}
