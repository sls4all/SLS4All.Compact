// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Storage;

namespace SLS4All.Compact.Components
{
    internal static class ValueEditorExtensions
    {
        public static RenderFragment RenderValueEditor(this StorageValue value, 
            StorageValue? valueDefault, 
            object? obj, 
            string? validationError = null, 
            string? cssScope = null, 
            bool isEditable = true,
            IInputValueTraits? traits = null,
            EventCallback valueEntered = default)
        {
            if (traits == null)
                traits = InputValueTraits.Create(value.Type);
            return builder =>
            {
                const int seqFirst = 1;
                var seq = seqFirst;
                ValueEditor<string> editor = default!;
                var actualValue = value.GetValue();
                var defaultValue = valueDefault?.GetValue();
                var displayedValue = actualValue ?? defaultValue;
                var type = typeof(ValueEditor<>).MakeGenericType(value.Type);
                builder.OpenComponent(seq++, type);
                if (cssScope != null)
                    builder.AddAttribute(seq++, cssScope, cssScope); // add this page scope to value editor
                builder.AddAttribute(seq++, nameof(editor.Title), value.Name.Name);
                var desc = value.Description.Description;
                if (actualValue != null)
                    builder.AddAttribute(seq++, "class", "property-value-set");
                else if (defaultValue != null)
                    builder.AddAttribute(seq++, "class", "property-value-set-parent");
                else
                    builder.AddAttribute(seq++, "class", "property-value-not-set");
                if (valueEntered.HasDelegate)
                    builder.AddAttribute(seq++, nameof(editor.ValueEntered), valueEntered);
                builder.AddAttribute(seq++, nameof(editor.Obj), obj);
                builder.AddAttribute(seq++, nameof(editor.Subtitle), desc);
                builder.AddAttribute(seq++, nameof(editor.Path), value.Path);
                builder.AddAttribute(seq++, nameof(editor.ValueObjectChanged), value.SetValue); // NOTE: before Value!
                builder.AddAttribute(seq++, nameof(editor.Value), actualValue);
                builder.AddAttribute(seq++, nameof(editor.IsEditable), value.Name.IsEditable && isEditable);
                builder.AddAttribute(seq++, nameof(editor.DefaultValue), defaultValue);
                builder.AddAttribute(seq++, nameof(editor.ValidationError), validationError);
                builder.AddAttribute(seq++, nameof(editor.Traits), traits);
                builder.AddAttribute(seq++, nameof(editor.Unit), value.Unit?.Unit);
                builder.AddAttribute(seq++, nameof(editor.ChildContent), new RenderFragment(builder =>
                {
                    var seq = seqFirst;
                    builder.OpenElement(seq++, "span");
                    if (cssScope != null)
                        builder.AddAttribute(seq++, cssScope);
                    builder.AddContent(seq++, displayedValue == null ? "not set" : traits.ValueToString(displayedValue));
                    builder.CloseElement();
                    if (value.Unit != null && displayedValue != null)
                    {
                        builder.OpenElement(seq++, "span");
                        if (cssScope != null)
                            builder.AddAttribute(seq++, cssScope);
                        builder.AddAttribute(seq++, "class", "property-suffix");
                        builder.AddContent(seq++, value.Unit.Unit);
                        builder.CloseElement();
                    }
                }));
                builder.CloseComponent();
            };
        }
    }
}
