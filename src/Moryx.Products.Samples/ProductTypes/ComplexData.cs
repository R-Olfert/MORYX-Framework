// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0

namespace Moryx.Products.Samples;


/// <summary>
/// To test complex data in text columns
/// </summary>
public class ComplexData
{
    //TODO: see SMAF-5082: Property 'Name' is filtered

    public string Content { get; set; }

    public string PropertyName { get; set; }

    public int Number { get; set; }

    public float Weight { get; set; }
}

