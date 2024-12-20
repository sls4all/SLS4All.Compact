// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Validation
{
    public sealed class ValidationHelper
    {
        private readonly object _obj;
        public List<ValidationError> ValidationErrors { get; set; } = new List<ValidationError>();
        public bool IsValid => ValidationErrors.Count == 0;

        public object Obj => _obj;

        public ValidationHelper(object obj)
        {
            _obj = obj;
        }

        public static implicit operator ValueTask<ValidationHelper>(ValidationHelper value)
            => ValueTask.FromResult(value);

        public async ValueTask<bool> Validate(
            IValidatable? value,
            ValidationContext context,
            [CallerArgumentExpression("value")] string valueName = null!,
            bool allowNull = false,
            string? valueInvalidMessage = null)
        {
            if (value == null)
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            var h = await value.Validate(context);
            if (!h.IsValid)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, valueInvalidMessage ?? "is set to value that itself is not valid", severity: h.ValidationErrors.Max(x => x.severity)));
                ValidationErrors.AddRange(h.ValidationErrors);
                return false;
            }
            return true;
        }

        public async ValueTask<bool> Validate<T, TValidable>(
            T? value,
            ValidationContext context,
            Func<T, ValueTask<TValidable?>> getValidable,
            [CallerArgumentExpression("value")] string valueName = null!,
            string? valueInvalidMessage = null,
            bool prependValueName = true)
            where TValidable : IValidatable
        {
            if (value == null)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)", prependValueName: prependValueName));
                return false;
            }
            var validable = await getValidable(value);
            if (validable == null)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty and existing value)", prependValueName: prependValueName));
                return false;
            }
            var h = await validable.Validate(context);
            if (!h.IsValid)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, valueInvalidMessage ?? "is set to value that itself is not valid", prependValueName: prependValueName));
                ValidationErrors.AddRange(h.ValidationErrors);
                return false;
            }
            return true;
        }

        public bool Validate<T>(
            T value,
            [CallerArgumentExpression("value")] string valueName = null!)
            where T: IStorageObjectReference
        {
            if (value.IsEmpty)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            return true;
        }

        public bool Validate(
            string? value,
            [CallerArgumentExpression("value")] string valueName = null!,
            bool allowNull = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            return true;
        }

        public bool Validate(
            bool? value,
            [CallerArgumentExpression("value")] string valueName = null!,
            bool allowNull = false)
        {
            if (value == null)
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            return true;
        }

        public bool Validate(
            TimeSpan? value,
            [CallerArgumentExpression("value")] string valueName = null!,
            TimeSpan? min = null,
            TimeSpan? max = null,
            TimeSpan? above = null,
            TimeSpan? below = null,
            bool allowNull = false)
        {
            if (value == null)
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            if (value < min)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be at least {min}"));
                return false;
            }
            if (value > max)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be at most {max}"));
                return false;
            }
            if (value <= above)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be above {above}"));
                return false;
            }
            if (value >= below)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be below {below}"));
                return false;
            }
            return true;
        }

        public bool ValidateOneOf<T>(
            T? value,
            T?[] values,
            [CallerArgumentExpression("value")] string valueName = null!,
            bool allowNull = false)
        {
            if (value == null)
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            if (Array.IndexOf(values, value) == -1)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be set to one of valid values"));
                return false;
            }
            return true;
        }

        public bool AddErrorForValue<T>(
            string message,
            T value,
            [CallerArgumentExpression("value")] string valueName = null!,
            bool prependValueName = true,
            ValidationSeverity severity = ValidationSeverity.Breaking)
        {
            ValidationErrors.Add(new ValidationError(_obj, valueName, message, prependValueName: prependValueName, severity: severity));
            return false;
        }

        public bool AddError(
            string message,
            string valueName,
            bool prependValueName = true,
            ValidationSeverity severity = ValidationSeverity.Breaking)
        {
            ValidationErrors.Add(new ValidationError(_obj, valueName, message, prependValueName: prependValueName, severity: severity));
            return false;
        }

        public bool Validate(
            decimal? value,
            [CallerArgumentExpression("value")] string valueName = null!,
            decimal? min = null,
            decimal? max = null,
            decimal? above = null,
            decimal? below = null,
            bool allowNull = false)
        {
            if (value == null)
            {
                if (allowNull)
                    return true;
                ValidationErrors.Add(new ValidationError(_obj, valueName, "must be set (non empty)"));
                return false;
            }
            if (value < min)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be at least {min}"));
                return false;
            }
            if (value > max)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be at most {max}"));
                return false;
            }
            if (value <= above)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be above {above}"));
                return false;
            }
            if (value >= below)
            {
                ValidationErrors.Add(new ValidationError(_obj, valueName, $"must be below {below}"));
                return false;
            }
            return true;
        }

        public ValidationHelper Clone()
        {
            var clone = new ValidationHelper(this);
            clone.ValidationErrors.AddRange(ValidationErrors);
            return clone;
        }

        public Dictionary<ValidationKey, ValidationValue> ToDictionary()
        {
            var res = new Dictionary<ValidationKey, ValidationValue>();
            foreach (var group in ValidationErrors.GroupBy(x => new ValidationKey(x.obj, x.valueName)))
            {
                var message = string.Join(Environment.NewLine, group.Select(x => x.valueName != "" && x.prependValueName ? $"value ({x.valueName}) {x.message}" : x.message));
                res.Add(group.Key, new ValidationValue(message, group.ToArray()));
            }
            return res;
        }

        public override string ToString()
        {
            var dict = ToDictionary();
            return string.Join(Environment.NewLine, dict.Values.Select(x => x.Message));
        }
    }
}
