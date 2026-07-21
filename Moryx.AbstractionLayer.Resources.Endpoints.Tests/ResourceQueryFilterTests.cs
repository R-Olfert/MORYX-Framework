// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0
using Moq;
using Moryx.AbstractionLayer.Resources.Endpoints.Models;
using Moryx.AbstractionLayer.TestTools.Resources;
using Moryx.AbstractionLayer.TestTools;
using NUnit.Framework;

namespace Moryx.AbstractionLayer.Resources.Endpoints.Tests;

[TestFixture]
public class ResourceQueryFilterTests
{
    private Mock<IResourceTypeTree> _treeMock;
    private Mock<IResourceTypeNode> _nodeMock;
    [SetUp]
    public void Setup()
    {
        _treeMock = new Mock<IResourceTypeTree>();
        _nodeMock = new Mock<IResourceTypeNode>();

        _nodeMock.SetupGet(x => x.ResourceType)
            .Returns(typeof(ReferenceResource));

        _nodeMock.SetupGet(x => x.PropertiesOfResourceType)
            .Returns(typeof(ReferenceResource).GetProperties());

        _treeMock.Setup(x => x[typeof(ReferenceResource).FullName])
            .Returns(_nodeMock.Object);
    }

    [Test(Description = "Match returns true when reference constraint is irrelevant")]
    public void MatchWithIrrelevantReferenceConstraintReturnsTrue()
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
        var filter = new ResourceQueryFilter(query, _treeMock.Object);
        var resource = new ReferenceResource();
        // Act
        var result = filter.Match(resource);
        // Assert
        Assert.That(result, Is.True);
    }

    [Test(Description = "Match returns false when reference collection is empty and constraint is NotEmpty")]
    public void MatchWithEmptyReferenceCollectionAndNotEmptyConstraintReturnsFalse()
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

        var filter = new ResourceQueryFilter(query, _treeMock.Object);
        // Act
        var result = filter.Match(resource);
        // Assert
        Assert.That(result, Is.False);
    }

    [Test(Description = "Match returns true when reference collection contains items and constraint is NotEmpty")]
    public void MatchWithNotEmptyReferenceCollectionAndNotEmptyConstraintReturnsTrue()
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
        var filter = new ResourceQueryFilter(query, _treeMock.Object);
        // Act
        var result = filter.Match(resource);
        // Assert
        Assert.That(result, Is.True);
    }

    [Test(Description = "Match returns true when reference collection is empty and constraint is NullOrEmpty")]
    public void MatchWithEmptyReferenceCollectionAndNullOrEmptyConstraintReturnsTrue()
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
        var filter = new ResourceQueryFilter(query, _treeMock.Object);
        // Act
        var result = filter.Match(resource);
        // Assert
        Assert.That(result, Is.True);
    }

    [Test(Description = "Match returns false when reference collection contains items and constraint is NullOrEmpty")]
    public void MatchWithNotEmptyReferenceCollectionAndNullOrEmptyConstraintReturnsFalse()
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
        var filter = new ResourceQueryFilter(query, _treeMock.Object);
        // Act
        var result = filter.Match(resource);
        // Assert
        Assert.That(result, Is.False);
    }

}
