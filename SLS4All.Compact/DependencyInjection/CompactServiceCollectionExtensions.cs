// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SLS4All.Compact.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace SLS4All.Compact.DependencyInjection
{
    public static class CompactServiceCollectionExtensions
    {
        public static HashSet<Assembly> ScanAssemblies { get; } =
        [
            typeof(CompactServiceCollectionExtensions).Assembly,
        ];

        public static Dictionary<Type, Type> PluginReplacements { get; } = new();

        private static readonly Lazy<Type[]> AllTypes = new Lazy<Type[]>(() =>
        {
            var res = new HashSet<Type>();
            GetAllTypesInternal(res);
            return res.ToArray();
        });

        private static void GetAssignableToTypes(Type type, HashSet<Type> results)
        {
            if (!results.Add(type))
                return;
            if (type.BaseType != null)
                GetAssignableToTypes(type.BaseType, results);
            foreach (var iface in type.GetInterfaces())
                GetAssignableToTypes(iface, results);
        }

        private static void GetAllTypesInternal(Type type, HashSet<Type> results)
        {
            if (!results.Add(type))
                return;
            foreach (var subType in type.GetNestedTypes())
                GetAllTypesInternal(subType, results);
        }

        private static void GetAllTypesInternal(HashSet<Type> results)
        {
            foreach (var ass in ScanAssemblies)
            {
                foreach (var type in ass.GetTypes())
                    GetAllTypesInternal(type, results);
            }
        }

        private static void GetAssignableFromTypes(Type type, HashSet<Type> results)
        {
            foreach (var other in AllTypes.Value)
            {
                if (type.IsAssignableFrom(other))
                    results.Add(other);
            }
        }

        private static IEnumerable<Type[]> CombineGenericArguments(Type definition, IReadOnlyList<Type[]> argVariants, int pos, Type[] result)
        {
            if (pos == argVariants.Count)
                yield return result;
            else
            {
                var variants = argVariants[pos];
                for (int i = 0; i < variants.Length; i++)
                {
                    foreach (var res in CombineGenericArguments(definition, argVariants, pos + 1, result.Append(variants[i]).ToArray()))
                        yield return res;
                }
            }
        }

        public static void AddAsImplementationAndInterfaces<TImplementation>(this IServiceCollection services, ServiceLifetime lifetime, Func<Type, bool>? serviceFilter = null)
            => AddAsImplementationAndInterfaces(
                services, 
                typeof(TImplementation), 
                lifetime,
                serviceFilter: serviceFilter);

        public static void AddAsObjectFactory(this IServiceCollection services, Type iface, Type implementation)
        {
            services.Add(
                new ServiceDescriptor(typeof(IObjectFactory<,>).MakeGenericType(iface, typeof(object)), 
                provider => ScopedServiceProviderObjectFactory.Create(
                    provider, 
                    implementation), 
                ServiceLifetime.Singleton));
        }

        public static void AddAsObjectFactory<T, TState>(this IServiceCollection services, Type iface, Func<IServiceProvider, TState, T> factory)
            where T : class
        {
            services.Add(
                new ServiceDescriptor(typeof(IObjectFactory<,>).MakeGenericType(iface, typeof(TState)),
                provider => ScopedServiceProviderObjectFactory.Create(provider, factory),
                ServiceLifetime.Singleton));
        }

        public static void AddAsImplementationAndInterfaces(this IServiceCollection services, Type implementation, ServiceLifetime lifetime, bool noPlugins = false, Func<Type, bool>? serviceFilter = null)
        {
            var actualImplementation = !noPlugins
                ? PluginReplacements.GetValueOrDefault(implementation, implementation)
                : implementation;

            services.Add(new ServiceDescriptor(actualImplementation, actualImplementation, lifetime));
            services.AddAsObjectFactory(actualImplementation, actualImplementation);

            if (actualImplementation != implementation && actualImplementation.IsAssignableTo(implementation)) // if actualImplementation is a subclass, register the implementation also, for compatability
            {
                services.Add(new ServiceDescriptor(implementation, provider => provider.GetRequiredService(implementation), lifetime));
                services.AddAsObjectFactory(implementation, actualImplementation);
            }

            foreach (var iface in actualImplementation.GetInterfaces())
            {
                if (serviceFilter?.Invoke(iface) == false)
                    continue;

                services.Add(new ServiceDescriptor(iface, provider => provider.GetRequiredService(actualImplementation), lifetime));
                services.AddAsObjectFactory(iface, actualImplementation);

                if (iface.IsGenericType)
                {
                    var args = iface.GetGenericArguments();
                    var def = iface.GetGenericTypeDefinition();
                    var defArgs = def.GetGenericArguments();
                    var argVariants = new List<Type[]>();
                    var set = new HashSet<Type>();
                    for (int i = 0; i < args.Length; i++)
                    {
                        var constraints = defArgs[i].GenericParameterAttributes;
                        var argType = args[i];
                        set.Clear();
                        set.Add(argType);
                        if ((constraints & GenericParameterAttributes.Covariant) == GenericParameterAttributes.Covariant)
                            GetAssignableToTypes(argType, set);
                        if ((constraints & GenericParameterAttributes.Contravariant) == GenericParameterAttributes.Contravariant)
                            GetAssignableFromTypes(argType, set);
                        argVariants.Add(set.ToArray());
                    }
                    foreach (var variant in CombineGenericArguments(def, argVariants, 0, Array.Empty<Type>()))
                    {
                        var combined = def.MakeGenericType(variant);
                        if (combined != iface)
                        {
                            services.Add(new ServiceDescriptor(combined, provider => provider.GetRequiredService(actualImplementation), lifetime));
                            services.AddAsObjectFactory(combined, actualImplementation);
                        }
                    }
                }
            }
        }

        public static void AddAsImplementationAndParents<TImplementation>(this IServiceCollection services, ServiceLifetime lifetime)
            => services.AddAsImplementationAndParents(typeof(TImplementation), lifetime);

        public static void AddAsImplementationAndParents(this IServiceCollection services, Type implementation, ServiceLifetime lifetime)
        {
            var actualImplementation = PluginReplacements.GetValueOrDefault(implementation, implementation);

            AddAsImplementationAndInterfaces(services, actualImplementation, lifetime, noPlugins: true);
            for (var baseType = actualImplementation.BaseType; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)
            {
                services.Add(new ServiceDescriptor(baseType, actualImplementation, lifetime));
                services.AddAsObjectFactory(baseType, actualImplementation);
            }
        }

        public static void AddAsImplementation<TImplementation>(this IServiceCollection services, ServiceLifetime lifetime)
            => services.AddAsImplementation(typeof(TImplementation), lifetime);

        public static void AddAsImplementation(this IServiceCollection services, Type implementation, ServiceLifetime lifetime)
        {
            var actualImplementation = PluginReplacements.GetValueOrDefault(implementation, implementation);
            services.Add(new ServiceDescriptor(actualImplementation, actualImplementation, lifetime));
            services.AddAsObjectFactory(actualImplementation, actualImplementation);
        }

        public static void AddAsService<TService, TImplementation>(this IServiceCollection services, ServiceLifetime lifetime)
            => services.AddAsService(typeof(TService), typeof(TImplementation), lifetime);

        public static void AddAsService(this IServiceCollection services, Type service, Type implementation, ServiceLifetime lifetime)
        {
            var actualImplementation = PluginReplacements.GetValueOrDefault(implementation, implementation);
            services.Add(new ServiceDescriptor(service, actualImplementation, lifetime));
            services.AddAsObjectFactory(service, actualImplementation);
        }
    }
}
