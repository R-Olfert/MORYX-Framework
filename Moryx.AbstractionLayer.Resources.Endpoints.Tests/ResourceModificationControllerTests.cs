// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Text;
using Moq;
using Moryx.AbstractionLayer.Resources.Endpoints.Models;
using Moryx.AbstractionLayer.TestTools;
using Moryx.AbstractionLayer.TestTools.Resources;
using Moryx.Runtime.Modules;
using NUnit.Framework;

namespace Moryx.AbstractionLayer.Resources.Endpoints.Tests;

[TestFixture]
internal class ResourceModificationControllerTests
{
    private Mock<IResourceManagement> _resourceManagementMock;
    private Mock<IResourceTypeTree> _resourceTypeTreeMock;
    private Mock<IModuleManager> _moduleManagerMock;
    private Mock<IServiceProvider> _serviceProviderMock;
    private ResourceModificationController _controller;
    private Mock<IResourceTypeNode> _nodeMock;

    [SetUp]
    public void SetUp()
    {
        _resourceManagementMock = new Mock<IResourceManagement>();
        _resourceTypeTreeMock = new Mock<IResourceTypeTree>();
        _moduleManagerMock = new Mock<IModuleManager>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        // 👇 Ye naya add karna hai
        _nodeMock = new Mock<IResourceTypeNode>();

        _nodeMock.SetupGet(x => x.ResourceType)
            .Returns(typeof(ReferenceResource));

        _nodeMock.SetupGet(x => x.PropertiesOfResourceType)
            .Returns(typeof(ReferenceResource).GetProperties());

        _resourceTypeTreeMock
            .Setup(x => x[typeof(ReferenceResource).FullName])
            .Returns(_nodeMock.Object);

        // Add this
        var facadeContainerMock = new Mock<IServerModule>();

            facadeContainerMock
                .As<IFacadeContainer<IResourceManagement>>()
                .Setup(x => x.Facade)
                .Returns(_resourceManagementMock.Object);

            _moduleManagerMock
                .Setup(x => x.AllModules)
                .Returns(new[] { facadeContainerMock.Object });

            // Then create controller
            _controller = new ResourceModificationController(
                _resourceManagementMock.Object,
                _resourceTypeTreeMock.Object,
                _moduleManagerMock.Object,
                _serviceProviderMock.Object);
    }

    [Test]
    public void GetResourcesWithIrrelevantReferenceConstraintReturnsResources()
    {
        // Arrange
        var query = new ResourceQuery
        {
            ReferenceCondition = new ReferenceFilter
            {
                Name = nameof(Resource.Children),
                ValueConstraint = ReferenceValue.Irrelevant
            }
        };
        var resource = new ReferenceResource();
        _resourceManagementMock
            .Setup(x => x.GetResourcesUnsafe<IResource>(It.IsAny<Func<IResource, bool>>()))
            .Returns((Func<IResource, bool> predicate) =>
            {
                return predicate(resource)
                    ? new IResource[] { resource }
                    : Array.Empty<IResource>();
            });
        // Act
        var result = _controller.GetResources(query);

        // Assert
        Assert.That(result.Value, Is.Not.Null);
    }

    [Test]
    public void GetResourcesReturnsEmptyWhenReferenceCollectionIsEmptyAndConstraintIsNotEmpty() 
    {
        // Arrange
        var references = new ReferenceCollectionMock<ISimpleResource>();

        var resource = new ReferenceResource
        {
            References = references
        };

        var query = new ResourceQuery
        {
            ReferenceCondition = new ReferenceFilter
            {
                Name = nameof(ReferenceResource.References),
                ValueConstraint = ReferenceValue.NotEmpty
            }
        };

        _resourceManagementMock
            .Setup(x => x.GetResourcesUnsafe<IResource>(It.IsAny<Func<IResource, bool>>()))
            .Returns((Func<IResource, bool> predicate) =>
            {
                return predicate(resource)
                    ? new IResource[] { resource }
                    : Array.Empty<IResource>();
            });

        // Act
        var result = _controller.GetResources(query);

        // Assert
        Assert.That(result.Value, Is.Empty);
        Assert.That(result.Value, Is.Not.Null);
    }

    [Test]
    public void GetResourcesReturnsResourcesWhenReferenceCollectionIsNotEmptyAndConstraintIsNotEmpty()
    {
        // Arrange
        var references = new ReferenceCollectionMock<ISimpleResource>();
        references.Add(new SimpleResource());

        var resource = new ReferenceResource
        {
            References = references
        };

        var query = new ResourceQuery
        {
            ReferenceCondition = new ReferenceFilter
            {
                Name = nameof(ReferenceResource.References),
                ValueConstraint = ReferenceValue.NotEmpty
            }
        };

        _resourceManagementMock
            .Setup(x => x.GetResourcesUnsafe<IResource>(It.IsAny<Func<IResource, bool>>()))
            .Returns((Func<IResource, bool> predicate) =>
            {
                return predicate(resource)
                    ? new IResource[] { resource }
                    : Array.Empty<IResource>();
            });

        _resourceManagementMock
            .Setup(x => x.ReadUnsafe<ResourceModel>(
                It.IsAny<long>(),
                It.IsAny<Func<Resource, ResourceModel>>()))
            .Returns((long _, Func<Resource, ResourceModel> accessor) =>
            {
                return accessor(resource);
            });

        // Act
        var result = _controller.GetResources(query);

        // Assert
        Assert.That(result.Result, Is.Null);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Length, Is.GreaterThan(0));
    }

    [Test]
    public void GetResourcesReturnsResourcesWhenReferenceCollectionIsEmptyAndConstraintIsNullOrEmpty()
    {
        // Arrange
        var references = new ReferenceCollectionMock<ISimpleResource>();

        var resource = new ReferenceResource
        {
            References = references
        };

        var query = new ResourceQuery
        {
            ReferenceCondition = new ReferenceFilter
            {
                Name = nameof(ReferenceResource.References),
                ValueConstraint = ReferenceValue.NullOrEmpty
            }
        };

        _resourceManagementMock
            .Setup(x => x.GetResourcesUnsafe<IResource>(It.IsAny<Func<IResource, bool>>()))
            .Returns((Func<IResource, bool> predicate) =>
            {
                return predicate(resource)
                    ? new IResource[] { resource }
                    : Array.Empty<IResource>();
            });

        _resourceManagementMock
            .Setup(x => x.ReadUnsafe<ResourceModel>(
                It.IsAny<long>(),
                It.IsAny<Func<Resource, ResourceModel>>()))
            .Returns((long _, Func<Resource, ResourceModel> accessor) =>
            {
                return accessor(resource);
            });

        // Act
        var result = _controller.GetResources(query);

        // Assert
        Assert.That(result.Result, Is.Null);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Length, Is.EqualTo(1));
    }

    [Test]
    public void GetResourcesReturnsEmptyWhenReferenceCollectionIsNotEmptyAndConstraintIsNullOrEmpty()
    {
        // Arrange
        var references = new ReferenceCollectionMock<ISimpleResource>();
        references.Add(new SimpleResource());
        var resource = new ReferenceResource
        {
            References = references
        };
        var query = new ResourceQuery
        {
            ReferenceCondition = new ReferenceFilter
            {
                Name = nameof(ReferenceResource.References),
                ValueConstraint = ReferenceValue.NullOrEmpty
            }
        };
        _resourceManagementMock
            .Setup(x => x.GetResourcesUnsafe<IResource>(It.IsAny<Func<IResource, bool>>()))
            .Returns((Func<IResource, bool> predicate) =>
            {
                return predicate(resource)
                    ? new IResource[] { resource }
                    : Array.Empty<IResource>();
            });

        _resourceManagementMock
            .Setup(x => x.ReadUnsafe<ResourceModel>(
                It.IsAny<long>(),
                It.IsAny<Func<Resource, ResourceModel>>()))
            .Returns((long _, Func<Resource, ResourceModel> accessor) =>
            {
                return accessor(resource);
            });

        // Act
        var result = _controller.GetResources(query);

        // Assert
        Assert.That(result.Result, Is.Null);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value.Length, Is.EqualTo(0));
    }

}
