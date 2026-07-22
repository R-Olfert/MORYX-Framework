// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0

using System.Reflection;
using Moryx.Container;
using Moryx.Tools;
// ReSharper disable ConvertToPrimaryConstructor

namespace Moryx.Products.Management;

/// <summary>
/// Mapper for columns of type <see cref="double"/>
/// </summary>
[FloatStrategyConfiguration]
[Component(LifeCycle.Transient, typeof(IPropertyMapper), Name = nameof(FloatColumnMapper))]
internal class FloatColumnMapper : ColumnMapper<double>
{
    private const int Precision = 8;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="targetType"></param>
    public FloatColumnMapper(Type targetType) : base(targetType)
    {
    }

    protected override IPropertyAccessor<object, double> CreatePropertyAccessor(PropertyInfo objectProp)
    {
        var innerAccessor = base.CreatePropertyAccessor(objectProp);
        return new RoundedDoublePropertyAccessor(innerAccessor, Precision);
    }

    private sealed class RoundedDoublePropertyAccessor : IPropertyAccessor<object, double>
    {
        private readonly IPropertyAccessor<object, double> _inner;
        private readonly int _precision;

        public RoundedDoublePropertyAccessor(IPropertyAccessor<object, double> inner, int precision)
        {
            _inner = inner;
            _precision = precision;
        }

        public string Name => _inner.Name;

        public PropertyInfo Property => _inner.Property;

        public double ReadProperty(object target)
        {
            var value = _inner.ReadProperty(target);
            return Math.Round(value, _precision, MidpointRounding.AwayFromZero);
        }

        public void WriteProperty(object target, double value)
        {
            _inner.WriteProperty(target, Math.Round(value, _precision, MidpointRounding.AwayFromZero));
        }
    }
}
