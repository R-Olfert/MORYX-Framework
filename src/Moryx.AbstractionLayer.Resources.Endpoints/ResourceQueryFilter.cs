// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Moryx.AbstractionLayer.Resources.Endpoints.Models;

namespace Moryx.AbstractionLayer.Resources.Endpoints;

internal sealed class ResourceQueryFilter
{
    private readonly ResourceQuery _query;
    private readonly IReadOnlyList<IResourceTypeNode> _typeNodes;
    private readonly IResourceTypeTree _resourceTypeTree;

    public ResourceQueryFilter(ResourceQuery query, IResourceTypeTree typeTree)
    {
        _query = query;
        _typeNodes = query.Types?.Select(typeName => typeTree[typeName]).Where(t => t != null).ToArray();
        _resourceTypeTree = typeTree;
    }

    public bool Match(Resource instance)
    {
        // Check type of instance, if filter is set
        if (_typeNodes != null && _typeNodes.All(tn => !tn.ResourceType.IsInstanceOfType(instance)))
        {
            return false;
        }

        // Next check for reference filters
        if (_query.ReferenceCondition == null)
        {
            return true;
        }

        var node = _resourceTypeTree[instance.GetType().FullName];

        var referenceCondition = _query.ReferenceCondition;
        var references = (from property in node.PropertiesOfResourceType
                          let att = property.GetCustomAttribute<ResourceReferenceAttribute>()
                          where att != null
                          select new { property, att }).ToList();

        // Find properties matching the condition
        PropertyInfo[] matches;
        if (!string.IsNullOrEmpty(referenceCondition.Name))
        {
            matches = references.Where(r => r.property.Name == referenceCondition.Name)
                .Select(r => r.property).ToArray();
        }
        else
        {
            matches = references
                .Where(r => r.att.RelationType == referenceCondition.RelationType && r.att.Role == referenceCondition.Role)
                .Select(r => r.property).ToArray();
        }
        if (matches.Length != 1)
        {
            return false;
        }

        if (referenceCondition.ValueConstraint == ReferenceValue.Irrelevant)
        {
            return true;
        }

        var propertyValue = matches[0].GetValue(instance);
        if (referenceCondition.ValueConstraint == ReferenceValue.NullOrEmpty)
        {
            if (propertyValue is IReferenceCollection referenceCollection)
            {
                return referenceCollection.UnderlyingCollection.Count == 0;
            }

            return propertyValue == null;
        }

        if (referenceCondition.ValueConstraint == ReferenceValue.NotEmpty)
        {
            if (propertyValue is IReferenceCollection referenceCollection)
            {
                return referenceCollection.UnderlyingCollection.Count > 0;
            }
            return propertyValue != null;
        }

        return true;
    }

}
