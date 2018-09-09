﻿using System;
using System.Reflection;
using AspectCore.DynamicProxy;
using AspectCore.DynamicProxy.Parameters;
using AspectCore.Extensions.Reflection;
using AspectCore.Configuration;
using AspectCore.Injector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace AspectCore.Extensions.DependencyInjection
{
    public static class ServiceCollectionBuildExtensions
    {
        [Obsolete("Use BuildAspectInjectorProvider to return AspectCore Injector, or Use BuildDynamicProxyServiceProvider to return MSDI ServiceProvider.", true)]
        public static IServiceProvider BuildAspectCoreServiceProvider(this IServiceCollection services)
        {
            return services.BuildDynamicProxyServiceProvider();
        }

        public static ServiceProvider BuildDynamicProxyServiceProvider(this IServiceCollection services)
        {
            return services.AddDynamicProxyCore().BuildServiceProvider();
        }

        public static ServiceProvider BuildDynamicProxyServiceProvider(this IServiceCollection services, bool validateScopes)
        {
            return services.AddDynamicProxyCore().BuildServiceProvider(validateScopes);
        }

        public static ServiceProvider BuildDynamicProxyServiceProvider(this IServiceCollection services, ServiceProviderOptions options)
        {
            return services.AddDynamicProxyCore().BuildServiceProvider(options);
        }

        public static IServiceCollection AddDynamicProxyCore(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var serviceProvider = services.TryAddDynamicProxyServices().BuildServiceProvider(false);

            var serviceValidator = new ServiceValidator(serviceProvider.GetRequiredService<IAspectValidatorBuilder>());
            var proxyTypeGenerator = serviceProvider.GetRequiredService<IProxyTypeGenerator>();

            IServiceCollection dynamicProxyServices = new ServiceCollection();
            foreach (var service in services)
            {
                if (serviceValidator.TryValidate(service, out Type implementationType))
                    dynamicProxyServices.Add(MakeProxyService(service, implementationType, proxyTypeGenerator));
                else
                    dynamicProxyServices.Add(service);
            }

            serviceProvider.Dispose();

            return dynamicProxyServices;
        }

        private static ServiceDescriptor MakeProxyService(ServiceDescriptor descriptor, Type implementationType, IProxyTypeGenerator proxyTypeGenerator)
        {
            var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
            if (serviceTypeInfo.IsClass)
            {
                return ServiceDescriptor.Describe(
                    descriptor.ServiceType,
                    proxyTypeGenerator.CreateClassProxyType(descriptor.ServiceType, implementationType),
                    descriptor.Lifetime);
            }
            else if (serviceTypeInfo.IsGenericTypeDefinition)
            {
                return ServiceDescriptor.Describe(
                    descriptor.ServiceType,
                    proxyTypeGenerator.CreateClassProxyType(implementationType, implementationType),
                    descriptor.Lifetime);
            }
            else
            {
                var proxyType = proxyTypeGenerator.CreateInterfaceProxyType(descriptor.ServiceType, implementationType);
                return ServiceDescriptor.Describe(
                    descriptor.ServiceType,
                    CreateFactory(descriptor, proxyType),
                    descriptor.Lifetime);
            }
        }

        private static Func<IServiceProvider, object> CreateFactory(ServiceDescriptor descriptor, Type proxyType)
        {
            var proxyConstructor = proxyType.GetTypeInfo().GetConstructor(new Type[] {typeof(IAspectActivatorFactory), descriptor.ServiceType});
            var reflector = proxyConstructor.GetReflector();
            if (descriptor.ImplementationInstance != null)
            {
                var implementationInstance = descriptor.ImplementationInstance;
                return provider => reflector.Invoke(provider.GetRequiredService<IAspectActivatorFactory>(), implementationInstance);
            }
            else if (descriptor.ImplementationFactory != null)
            {
                var implementationFactory = descriptor.ImplementationFactory;
                return provider => reflector.Invoke(provider.GetRequiredService<IAspectActivatorFactory>(), implementationFactory(provider));
            }
            else
            {
                var implementationType = descriptor.ImplementationType;
                return provider =>
                {
                    var aspectActivatorFactory = provider.GetRequiredService<IAspectActivatorFactory>();
                    var instance = ActivatorUtilities.CreateInstance(provider, implementationType);
                    return reflector.Invoke(aspectActivatorFactory, instance);
                };
            }
        }
    }
}