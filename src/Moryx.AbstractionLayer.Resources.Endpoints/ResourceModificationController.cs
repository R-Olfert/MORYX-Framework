// Copyright (c) 2026 Phoenix Contact GmbH & Co. KG
// Licensed under the Apache License, Version 2.0

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.ServerSentEvents;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moryx.AbstractionLayer.Resources.Endpoints.Models;
using Moryx.AbstractionLayer.Resources.Endpoints.Properties;
using Moryx.AspNetCore;
using Moryx.Configuration;
using Moryx.Runtime.Modules;
using Moryx.Serialization;
using Moryx.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Moryx.AbstractionLayer.Resources.Endpoints;

/// <summary>
/// Definition of a REST API on the <see cref="IResourceManagement"/> facade.
/// </summary>
[ApiController]
[Route("api/moryx/resources/")]
[Produces("application/json")]
//TODO: Rename to ResourceManagementController in next major version
public class ResourceModificationController : ControllerBase
{
    private readonly IResourceManagement _resourceManagement;
    private readonly IResourceTypeTree _resourceTypeTree;
    private readonly ResourceSerialization _serialization;

    private static JsonSerializerSettings CreateSerializerSettings()
    {
        var serializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };
        serializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        return serializerSettings;
    }

    public ResourceModificationController(IResourceManagement resourceManagement,
        IResourceTypeTree resourceTypeTree,
        IModuleManager moduleManager,
        IServiceProvider serviceProvider)
    {
        _resourceManagement = resourceManagement ?? throw new ArgumentNullException(nameof(resourceManagement));
        _resourceTypeTree = resourceTypeTree ?? throw new ArgumentNullException(nameof(resourceTypeTree));
        var module = moduleManager.AllModules.FirstOrDefault(module => module is IFacadeContainer<IResourceManagement>);
        _serialization = new ResourceSerialization(module.Container, serviceProvider);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("types")]
    [Authorize(Policy = ResourcePermissions.CanViewTypeTree)]
    public ActionResult<ResourceTypeModel> GetTypeTree()
    {
        var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);
        return converter.ConvertType(_resourceTypeTree.RootType);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Authorize(Policy = ResourcePermissions.CanViewDetails)]
    public ActionResult<ResourceModel[]> GetDetailsBatch([FromQuery] long[] ids)
    {
        var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);

        if (ids is null || ids.Length == 0)
        {
            ids = _resourceManagement.GetResources<IResource>().Select(r => r.Id).ToArray();
        }

        return ids.Select(id => _resourceManagement.ReadUnsafe(id, r => converter.GetDetails(r)))
            .Where(details => details != null).ToArray();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("query")]
    [Authorize(Policy = ResourcePermissions.CanViewTree)]
    public ActionResult<ResourceModel[]> GetResources(ResourceQuery query)
    {
        var filter = new ResourceQueryFilter(query, _resourceTypeTree);
        var resourceProxies = _resourceManagement.GetResourcesUnsafe<IResource>(r => filter.Match(r as Resource)).ToArray();

        var converter = new ResourceQueryConverter(_resourceTypeTree, _serialization, query);
        var values = resourceProxies.Select(p => _resourceManagement.ReadUnsafe(p.Id, r => converter.QueryConversion(r))).Where(details => details != null).ToArray();
        return values;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("{id}")]
    [Authorize(Policy = ResourcePermissions.CanViewDetails)]
    public ActionResult<ResourceModel> GetDetails(long id)
    {
        var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);
        var resourceModel = _resourceManagement.ReadUnsafe(id, converter.GetDetails);
        if (resourceModel is null)
        {
            return NotFound(new MoryxExceptionResponse { Title = string.Format(CultureInfo.CurrentCulture, Strings.ResourceNotFoundException_ById_Message, id) });
        }

        return resourceModel;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("{id}/invoke/{method}")]
    [Authorize(Policy = ResourcePermissions.CanInvokeMethod)]
    public async Task<ActionResult<Entry>> InvokeMethod(long id, string method, Entry parameters)
    {
        if (_resourceManagement.GetResourcesUnsafe<IResource>(r => r.Id == id) is null)
        {
            return NotFound(new MoryxExceptionResponse { Title = string.Format(CultureInfo.CurrentCulture, Strings.ResourceNotFoundException_ById_Message, id) });
        }

        Entry entry = null;
        try
        {
            await _resourceManagement.ModifyUnsafeAsync(id, r =>
            {
                entry = EntryConvert.InvokeMethod(r.Descriptor, new MethodEntry { Name = method, Parameters = parameters }, _serialization);
                return Task.FromResult(true);
            });
        }
        catch (MissingMethodException)
        {
            return BadRequest("Method could not be invoked. Please check spelling and access modifier (has to be `public` or `internal`).");
        }
        catch
        {
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return entry;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("types/{type}")]
    [Authorize(Policy = ResourcePermissions.CanAdd)]
    public Task<ActionResult<ResourceModel>> ConstructWithParameters(string type, string method = null, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] Entry arguments = null)
    {
        var trustedType = WebUtility.HtmlEncode(type);
        if (method is null)
        {
            return Construct(trustedType);
        }

        return Construct(trustedType, new MethodEntry { Name = method, Parameters = arguments });
    }

    private async Task<ActionResult<ResourceModel>> Construct(string type)
    {
        Resource resource;
        try
        {
            resource = (Resource)Activator.CreateInstance(_resourceTypeTree[type].ResourceType);
        }
        catch (Exception)
        {
            return NotFound(new MoryxExceptionResponse
            {
                Title = Strings.ResourceManagementController_ResourceNotFound
            });
        }

        ValueProviderExecutor.Execute(resource, new ValueProviderExecutorSettings()
            .AddFilter(new DataMemberAttributeValueProviderFilter(false))
            .AddDefaultValueProvider());

        var model = new ResourceToModelConverter(_resourceTypeTree, _serialization).GetDetails(resource);
        model.Methods = []; // Reset methods because they can not be invoked on new objects
        return model;
    }

    private async Task<ActionResult<ResourceModel>> Construct(string type, MethodEntry method)
    {
        try
        {
            var id = await _resourceManagement.CreateUnsafeAsync(_resourceTypeTree[type].ResourceType, resource =>
            {
                EntryConvert.InvokeMethod(resource, method, _serialization);
                return Task.CompletedTask;
            });
            return GetDetails(id);
        }
        catch (Exception e)
        {
            if (e is ArgumentException or SerializationException or ValidationException)
            {
                return BadRequest(e.Message);
            }

            throw;
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [Authorize(Policy = ResourcePermissions.CanAdd)]
    public async Task<ActionResult<ResourceModel>> Save(ResourceModel model)
    {
        if (_resourceManagement.GetResourcesUnsafe<IResource>(r => r.Id == model.Id).Any())
        {
            return Conflict($"The resource '{model.Id}' already exists.");
        }

        try
        {
            var id = await _resourceManagement.CreateUnsafeAsync(_resourceTypeTree[model.Type].ResourceType, async (r) =>
            {
                var resourcesToSave = new HashSet<long>();
                var resourceCache = new Dictionary<long, Resource>();
                var converter = new ModelToResourceConverter(_resourceManagement, _resourceTypeTree, _serialization);
                await converter.FromModel(model, resourcesToSave, resourceCache, r);
                foreach (var resource in resourcesToSave.Skip(1))
                {
                    await _resourceManagement.ModifyUnsafeAsync(resource, r => Task.FromResult(true));
                }
            });

            return GetDetails(id);
        }
        catch (Exception e)
        {
            if (e is ArgumentException or SerializationException or ValidationException)
            {
                return BadRequest(e.Message);
            }

            throw;
        }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [Route("{id}")]
    [Authorize(Policy = ResourcePermissions.CanEdit)]
    public ActionResult<ResourceModel> Update(long id, ResourceModel model)
    {
        if (_resourceManagement.GetResourcesUnsafe<IResource>(r => r.Id == id) is null)
        {
            return NotFound(new MoryxExceptionResponse { Title = string.Format(CultureInfo.CurrentCulture, Strings.ResourceNotFoundException_ById_Message, id) });
        }

        try
        {
            _resourceManagement.ModifyUnsafeAsync(id, async (r) =>
            {
                var resourcesToSave = new HashSet<long>();
                var resourceCache = new Dictionary<long, Resource>();
                var converter = new ModelToResourceConverter(_resourceManagement, _resourceTypeTree, _serialization);
                await converter.FromModel(model, resourcesToSave, resourceCache, r);
                resourcesToSave.ForEach(id => _resourceManagement.ModifyUnsafeAsync(id, _ => Task.FromResult(true)));
                return true;
            });
        }
        catch (Exception e)
        {
            if (e is ArgumentException or SerializationException or ValidationException)
            {
                return BadRequest(e.Message);
            }

            throw;
        }

        return GetDetails(id);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status417ExpectationFailed)]
    [Route("{id}")]
    [Authorize(Policy = ResourcePermissions.CanDelete)]
    public async Task<ActionResult> Remove(long id)
    {
        var existing = _resourceManagement.GetResourcesUnsafe<IResource>(r => r.Id == id);
        if (!existing.Any())
        {
            return NotFound(new MoryxExceptionResponse { Title = string.Format(CultureInfo.CurrentCulture, Strings.ResourceNotFoundException_ById_Message, id) });
        }

        var deleted = await _resourceManagement.DeleteAsync(id);
        if (!deleted)
        {
            return Conflict($"Unable to delete {id}");
        }

        return Accepted();
    }

    private sealed class ResourceQueryFilter
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
                return propertyValue == null || (propertyValue as IReferenceCollection)?.UnderlyingCollection.Count == 0;
            }

            if (referenceCondition.ValueConstraint == ReferenceValue.NotEmpty)
            {
                return (propertyValue as IReferenceCollection)?.UnderlyingCollection.Count > 0 || propertyValue != null;
            }

            return true;
        }
    }

    [HttpGet("stream")]
    [ProducesResponseType(typeof(ResourceModel), StatusCodes.Status200OK)] // TODO: kontrollieren ob typeof korrekt ist, da hier bei public async Task<...> nichts angegeben ist
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = ResourcePermissions.CanDelete)]
    public async Task OperationStream(CancellationToken cancellationToken)
    {
        var response = Response; // ein HttpResponse-Objekt, über welches SSE-Stream konfiguriert und an Client geschrieben wird
        response.Headers["Content-Type"] = "text/event-stream"; // kennzeichnet das HttpResponse-Objekt als SSE-Stream

        var operationsChannel = Channel.CreateUnbounded<SseItem<string>>(); // ein unbegrenzter Channel (Sammelstelle von Nachrichten von EventHandler an SSE-Schleife) wo immer zwei strings gespeichert werden, der erste für den Namen des SSE-Events u. der zweite für die JSON-Daten des Ereignisses

        // Define event handlling
        var addedEventHandler = new EventHandler<IResource>((_, eventArgs) => // neuer EventHandler wird mit Datentyp IResource mit den Parametern (Absender wird nicht verwendet, Daten von ausgelöstem Event) erstellt u. soll nachfolgenden Code ausführen, eventArgs ist quasi der Name des IResource Objektes
        {
            var id = eventArgs.Id;
            var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);
            var resourceModel = _resourceManagement.ReadUnsafe(id, r => converter.GetDetails(r));
            if (resourceModel is null)
            {
                return; // NotFound(new MoryxExceptionResponse { Title = string.Format(Strings.ResourceNotFoundException_ById_Message, id) });
            }
            var json = JsonConvert.SerializeObject(resourceModel, _serializerSettings); // addedOperartion wird von C#-Objekt in Json-String umgewandelt um in den Channel geschrieben zu werden, deswegen auch String
            operationsChannel.Writer.TryWrite(new Tuple<string, string>("Added", json)); // es wird versucht, den Namen des SSE-Events und die Daten, aus einer neu erstellten Tupel welche beides zu einer Nachricht für den Channel zusammenfasst, davon sofort in den Channel zu schreiben, danach wird ein boolscher Wert zurückgegeben ob die Nachricht angenommen wurde oder nicht
        });
        _resourceManagement.ResourceAdded += addedEventHandler;

        var removedEventHandler = new EventHandler<IResource>((_, eventArgs) =>
        {
            var id = eventArgs.Id;
            var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);
            var resourceModel = _resourceManagement.ReadUnsafe(id, r => converter.GetDetails(r));
            if (resourceModel is null)
            {
                return; // NotFound(new MoryxExceptionResponse { Title = string.Format(Strings.ResourceNotFoundException_ById_Message, id) });
            }
            var json = JsonConvert.SerializeObject(resourceModel, _serializerSettings);
            operationsChannel.Writer.TryWrite(new Tuple<string, string>("Removed", json));
        });
        _resourceManagement.ResourceRemoved += removedEventHandler;

        var changedEventHandler = new EventHandler<IResource>((_, eventArgs) =>
        {
            var id = eventArgs.Id;
            var converter = new ResourceToModelConverter(_resourceTypeTree, _serialization);
            var resourceModel = _resourceManagement.ReadUnsafe(id, r => converter.GetDetails(r));
            if (resourceModel is null)
            {
                return; // NotFound(new MoryxExceptionResponse { Title = string.Format(Strings.ResourceNotFoundException_ById_Message, id) });
            }
            var json = JsonConvert.SerializeObject(resourceModel, _serializerSettings); // addedOperartion wird von C#-Objekt in Json-String umgewandelt um in den Channel geschrieben zu werden, deswegen auch String
            operationsChannel.Writer.TryWrite(new Tuple<string, string>("Changed", json)); // es wird versucht, den Namen des SSE-Events und die Daten, aus einer neu erstellten Tupel welche beides zu einer Nachricht für den Channel zusammenfasst, davon sofort in den Channel zu schreiben, danach wird ein boolscher Wert zurückgegeben ob die Nachricht angenommen wurde oder nicht
        });
        _resourceManagement.ResourceChanged += changedEventHandler;

        // folgendes Event CapabilitiesChanged braucht man erstmal nicht
        //var capaeventhandler = new eventhandler<icapabilities>((_, eventargs) =>
        //{
        //    var capaoperation = new
        //    {
        //        resourcemodel = converter.tomodel(eventargs.operation);
        //};
        //var json = jsonconvert.serializeobject(capaoperation, _serializersettings);
        //operationschannel.writer.trywrite(new tuple<string, string>(nameof(operationtypes.update), json));
        //        });
        //_resourcemanagement.capabilitieschanged += capaeventhandler;

        try
        {
            while (!cancellationToken.IsCancellationRequested) // solange kein Abbruch gefordert, also canelationToken=false
            {
                var changes = await operationsChannel.Reader.ReadAsync(cancellationToken); // auf neue Nachricht im Channel warten u. diese dann lesen

                await response.WriteAsync($"event: {changes.Item1}\n", cancellationToken); // Namen von SSE-Event in HttpResponse-Objekt reinschreiben/darüber fortlaufend senden ohne dass es sofort wieder gelschlossen wird, cancellationToken ermöglicht den Abbruch
                await response.WriteAsync($"data: {changes.Item2}\r\n", cancellationToken); // JSON-Daten von SSE-Event in HttpResponse-Objekt reinschreiben/darüber fortlaufend senden ohne dass es sofort wieder gelschlossen wird, cancellationToken ermöglicht den Abbruch
            }
        }
        // Fehler werden nicht behandelt
        catch (OperationCanceledException) // Fehler typischerweise durch Abbruch von CancellationToken
        { }
        catch (ChannelClosedException) // Fehler typischerweise, wenn Channel geschlossen wurde, aber noch daraus gelesen werden soll
        { }
        catch (InvalidCastException) // Fehler typischerweise, wenn Typumnwandlung ungültig ist
        { }
        finally
        {
            // Events abmelden
            _resourceManagement.ResourceAdded -= addedEventHandler;
            _resourceManagement.ResourceRemoved -= removedEventHandler;
            _resourceManagement.ResourceChanged -= changedEventHandler;
            // _resourceManagement.CapabilitiesChanged -= capaEventHandler;

            operationsChannel.Writer.TryComplete(); // beendet das Schreiben in den Channel
        }

        await response.CompleteAsync(); // schließt SSE-Stream ordentlich
    }
}
