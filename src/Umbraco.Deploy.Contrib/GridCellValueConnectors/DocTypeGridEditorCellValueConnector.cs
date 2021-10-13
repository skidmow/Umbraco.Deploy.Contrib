using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Deploy;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Deploy.Core.Connectors.ValueConnectors.Services;
using Umbraco.Extensions;

namespace Umbraco.Deploy.Contrib.Connectors.GridCellValueConnectors
{
    public class DocTypeGridEditorCellValueConnector : IGridCellValueConnector
    {
        private readonly ILogger<DocTypeGridEditorCellValueConnector> _logger;
        private readonly IContentTypeService _contentTypeService;
        private readonly IShortStringHelper _shortStringHelper;
        private readonly Lazy<ValueConnectorCollection> _valueConnectorsLazy;

        private ValueConnectorCollection ValueConnectors => _valueConnectorsLazy.Value;

        public DocTypeGridEditorCellValueConnector(
            ILogger<DocTypeGridEditorCellValueConnector> logger,
            IContentTypeService contentTypeService,
            IShortStringHelper shortStringHelper,
            Lazy<ValueConnectorCollection> valueConnectors)
        {
            _logger = logger;
            _contentTypeService = contentTypeService ?? throw new ArgumentNullException(nameof(contentTypeService));
            _shortStringHelper = shortStringHelper;
            _valueConnectorsLazy = valueConnectors ?? throw new ArgumentNullException(nameof(valueConnectors));
        }

        public bool IsConnector(string view) => !string.IsNullOrWhiteSpace(view) && view.Contains("doctypegrideditor");

        public string GetValue(GridValue.GridControl gridControl, ICollection<ArtifactDependency> dependencies)
        {
            // cancel if there's no values
            if (gridControl.Value == null || gridControl.Value.HasValues == false) return null;

            _logger.LogDebug($"GetValue - Grid Values: {gridControl.Value}");

            var docTypeGridEditorContent = JsonConvert.DeserializeObject<DocTypeGridEditorValue>(gridControl.Value.ToString());

            // if an 'empty' dtge item has been added - it has no ContentTypeAlias set .. just return and don't throw.
            if (docTypeGridEditorContent == null || string.IsNullOrWhiteSpace(docTypeGridEditorContent.ContentTypeAlias))
            {
                _logger.LogDebug("GetValue - DTGE Empty without ContentTypeAlias");
                return null;
            }

            _logger.LogDebug($"GetValue - ContentTypeAlias - {docTypeGridEditorContent.ContentTypeAlias}");

            // check if the doc type exist - else abort packaging
            var contentType = _contentTypeService.Get(docTypeGridEditorContent.ContentTypeAlias);

            if (contentType == null)
            {
                _logger.LogDebug("GetValue - Missing ContentType");
                throw new InvalidOperationException($"Could not resolve the Content Type for the Doc Type Grid Editor property: {docTypeGridEditorContent.ContentTypeAlias}");
            }

            // add content type as a dependency
            dependencies.Add(new ArtifactDependency(contentType.GetUdi(), false, ArtifactDependencyMode.Match));

            // find all properties
            var propertyTypes = contentType.CompositionPropertyTypes;

            foreach (var propertyType in propertyTypes)
            {
                _logger.LogDebug($"GetValue - PropertyTypeAlias - {propertyType.Alias}");

                // test if there's a value for the given property
                if (IsValueNull(docTypeGridEditorContent, propertyType, out var value))
                {
                    continue;
                }

                // if the value is an Udi then add it as a dependency
                if (AddUdiDependency(dependencies, value))
                {
                    continue;
                }

                JToken jtokenValue = null;
                var parsedValue = string.Empty;

                // throws if not found - no need for a null check
                var propValueConnector = ValueConnectors.Get(propertyType);

                _logger.LogDebug($"GetValue - PropertyValueConnectorAlias - {propValueConnector.PropertyEditorAliases}");
                _logger.LogDebug($"GetValue - propertyTypeValue - {value}");

                //properties like MUP / Nested Content seems to be in json and it breaks, so pass it back as jtokenValue right away
                if (IsJson(value))
                {
                    jtokenValue = GetJTokenValue(value);
                    _logger.LogDebug($"GetValue - Inner JtokenValue - Eg MUP/NestedContent {jtokenValue}");
                }
                else
                {
                    parsedValue = propValueConnector.ToArtifact(value, propertyType, dependencies);

                    _logger.LogDebug($"GetValue - ParsedValue - {parsedValue}");

                    if (IsJson(parsedValue))
                    {
                        // if that's the case we'll need to add it as a json object instead of string to avoid it being escaped
                        jtokenValue = GetJTokenValue(parsedValue);
                    }
                }

                if (jtokenValue != null)
                {
                    _logger.LogDebug($"GetValue - JtokenValue - {jtokenValue}");
                    docTypeGridEditorContent.Value[propertyType.Alias] = jtokenValue;
                }
                else
                {
                    docTypeGridEditorContent.Value[propertyType.Alias] = parsedValue;
                }
            }

            var resolvedValue = JsonConvert.SerializeObject(docTypeGridEditorContent);

            _logger.LogDebug($"GetValue - ResolvedValue - {resolvedValue}");

            return resolvedValue;
        }

        public void SetValue(GridValue.GridControl gridControl)
        {
            // cancel if there's no values
            if (string.IsNullOrWhiteSpace(gridControl.Value.ToString()))
            {
                return;
            }

            // For some reason the control value isn't properly parsed so we need this extra step to parse it into a JToken
            gridControl.Value = JToken.Parse(gridControl.Value.ToString());

            _logger.LogDebug($"SetValue - GridControlValue - {gridControl.Value}");

            var docTypeGridEditorContent = JsonConvert.DeserializeObject<DocTypeGridEditorValue>(gridControl.Value.ToString());

            if (docTypeGridEditorContent == null)
            {
                return;
            }

            _logger.LogDebug($"SetValue - ContentTypeAlias - {docTypeGridEditorContent.ContentTypeAlias}");

            // check if the doc type exist - else abort packaging
            var contentType = _contentTypeService.Get(docTypeGridEditorContent.ContentTypeAlias);

            if (contentType == null)
            {
                throw new InvalidOperationException($"Could not resolve the Content Type for the Doc Type Grid Editor property: {docTypeGridEditorContent.ContentTypeAlias}");
            }

            _logger.LogDebug($"SetValue - ContentType - {contentType}");

            // find all properties
            var propertyTypes = contentType.CompositionPropertyTypes;

            foreach (var propertyType in propertyTypes)
            {
                JToken jtokenValue = null;

                _logger.LogDebug($"SetValue - PropertyEditorAlias -- {propertyType.PropertyEditorAlias}");

                if (!docTypeGridEditorContent.Value.TryGetValue(propertyType.Alias, out object value) || value == null)
                {
                    _logger.LogDebug("SetValue - Value Is Null");
                    continue;
                }

                // throws if not found - no need for a null check
                var propValueConnector = ValueConnectors.Get(propertyType);
                propValueConnector.FromArtifact(value.ToString(), propertyType, "");

                _logger.LogDebug($"SetValue - PropertyValueConnecterType - {propValueConnector.GetType()}");
                _logger.LogDebug($"SetValue - Value - {value}");

                // test if there's a value for the given property
                object convertedValue;
                //don't convert if it's json (MUP/Nested) / udi (Content/Media/Multi Pickers) / guid (form picker) / rte / textstring values
                if (IsJson(value) || IsUdi(value) || IsGuid(value) || IsText(propertyType.PropertyEditorAlias))
                {
                    _logger.LogDebug($"SetValue - IsJsonValue / IsUdiValue / IsGuidValue / IsTextValue - {value}");
                    convertedValue = CleanValue(propertyType.PropertyEditorAlias, value);
                }
                else
                {
                    //using mockContent to get the converted values
                    var mockProperty = new Property(propertyType);
                    var mockContent = new Content("mockContentGrid", -1, new ContentType(_shortStringHelper, -1), new PropertyCollection(new List<Property> { mockProperty }));
                    convertedValue = mockContent.GetValue(mockProperty.Alias);
                    _logger.LogDebug($"SetValue - ConvertedValue - Before - {convertedValue}");
                }

                // integers needs to be converted into strings for DTGE to work
                if (convertedValue is int)
                {
                    docTypeGridEditorContent.Value[propertyType.Alias] = convertedValue.ToString();
                }
                else if (convertedValue == null)
                {
                    //Assign the null back - otherwise the check for JSON will fail as we cant convert a null to a string
                    //NOTE: LinkPicker2 for example if no link set is returning a null as opposed to empty string
                    docTypeGridEditorContent.Value[propertyType.Alias] = null;
                }
                else
                {
                    _logger.LogDebug($"SetValue - ConvertedValue - After - {convertedValue}");

                    if (IsJson(convertedValue))
                    {
                        // test if the value is a json object (thus could be a nested complex editor)
                        // if that's the case we'll need to add it as a json object instead of string to avoid it being escaped
                        jtokenValue = GetJTokenValue(convertedValue);
                    }

                    if (jtokenValue != null)
                    {
                        _logger.LogDebug($"SetValue - jtokenValue - {jtokenValue}");
                        docTypeGridEditorContent.Value[propertyType.Alias] = jtokenValue;
                    }
                    else
                    {
                        docTypeGridEditorContent.Value[propertyType.Alias] = convertedValue;
                    }
                }
            }

            var jtokenObj = JToken.FromObject(docTypeGridEditorContent);
            _logger.LogDebug($"SetValue - jtokenObject - {jtokenObj}");
            gridControl.Value = jtokenObj;
        }

        private static JToken GetJTokenValue(object value) => value != null && IsJson(value) ? JToken.Parse(value.ToString()) : null;

        private static bool IsJson(object value) => value != null && value.ToString().DetectIsJson();

        private static bool IsGuid(object value) => Guid.TryParse(value.ToString(), out _);

        private static bool IsUdi(object value) =>
            //for picker like content/media either single or multi, it comes with udi values
            value.ToString().Contains("umb://");

        private static bool IsText(string editorAlias) =>
            //if it's either RTE / Textstring data type
            IsRichtext(editorAlias) || IsTextstring(editorAlias);

        private static bool IsRichtext(string editorAlias) => editorAlias.InvariantEquals("Umbraco.TinyMCE");

        private static bool IsTextstring(string editorAlias) => editorAlias.InvariantEquals("Umbraco.TextBox");

        private string CleanValue(string editorAlias, object value) =>
            //can't tell why textstring have got a weird character 's' in front coming from deploy? so removing first char if that's the case
            IsTextstring(editorAlias) ? value.ToString().Substring(1) : value.ToString();

        private bool AddUdiDependency(ICollection<ArtifactDependency> dependencies, object value)
        {
            if (UdiParser.TryParse(value.ToString(), out var udi))
            {
                _logger.LogDebug($"GetValue - Udi Dependency - {udi}");
                dependencies.Add(new ArtifactDependency(udi, false, ArtifactDependencyMode.Match));
                return true;
            }

            return false;
        }

        private bool IsValueNull(DocTypeGridEditorValue docTypeGridEditorContent, IPropertyType propertyType, out object objVal)
        {
            if (!docTypeGridEditorContent.Value.TryGetValue(propertyType.Alias, out objVal) || objVal == null)
            {
                _logger.LogDebug("GetValue - Value is null");
                return true;
            }

            return false;
        }

        private class DocTypeGridEditorValue
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }

            [JsonProperty("dtgeContentTypeAlias")]
            public string ContentTypeAlias { get; set; }

            [JsonProperty("value")]
            public Dictionary<string, object> Value { get; set; }
        }
    }
}
