﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Core.Deploy;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace Umbraco.Deploy.Contrib.Connectors.ValueConnectors
{
    /// <summary>
    /// Represents a value connector for the Cogworks.Meganav property editor.
    /// </summary>
    /// <seealso cref="Umbraco.Core.Deploy.IValueConnector" />
    public class MeganavValueConnector : IValueConnector
    {
        /// <summary>
        /// The entity service.
        /// </summary>
        private readonly IEntityService _entityService;

        /// <summary>
        /// Gets the property editor aliases that the value converter supports by default.
        /// </summary>
        public IEnumerable<string> PropertyEditorAliases => new[] { "Cogworks.Meganav" };

        /// <summary>
        /// Initializes a new instance of the <see cref="MeganavValueConnector"/> class.
        /// </summary>
        /// <param name="entityService">The entity service.</param>
        /// <exception cref="ArgumentNullException">entityService</exception>
        public MeganavValueConnector(IEntityService entityService)
        {
            _entityService = entityService ?? throw new ArgumentNullException("entityService");
        }

        /// <summary>
        /// Gets the deploy property corresponding to a content property.
        /// </summary>
        /// <param name="property">The content property.</param>
        /// <param name="dependencies">The content dependencies.</param>
        /// <returns>
        /// The deploy property value.
        /// </returns>
        public string GetValue(Property property, ICollection<ArtifactDependency> dependencies)
        {
            var value = property.Value as string;
            if (String.IsNullOrWhiteSpace(value) || !StringExtensions.DetectIsJson(value))
            {
                return null;
            }

            // Parse links and convert ID to UDI
            var rootLinks = ParseLinks(JArray.Parse(value), dependencies, Direction.ToArtifact);

            return rootLinks.ToString(Formatting.None);
        }

        /// <summary>
        /// Sets a content property value using a deploy property.
        /// </summary>
        /// <param name="content">The content item.</param>
        /// <param name="alias">The property alias.</param>
        /// <param name="value">The deploy property value.</param>
        public void SetValue(IContentBase content, string alias, string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                // Save empty value
                content.SetValue(alias, value);
                return;
            }

            if (!StringExtensions.DetectIsJson(value))
            {
                // Skip invalid values
                return;
            }

            // Parse links and convert UDI back to local ID
            var rootLinks = ParseLinks(JArray.Parse(value), null, Direction.FromArtifact);

            content.SetValue(alias, rootLinks.ToString(Formatting.None));
        }

        private JArray ParseLinks(JArray links, ICollection<ArtifactDependency> dependencies, Direction direction)
        {
            foreach (var link in links)
            {
                if (direction == Direction.ToArtifact)
                {
                    GuidUdi guidUdi = null;
                    var id = link.Value<int>("id");
                    if (id != 0)
                    {
                        var keyForId = _entityService.GetKeyForId(id, UmbracoObjectTypes.Document);
                        if (keyForId.Success)
                        {
                            guidUdi = new GuidUdi(Constants.UdiEntityType.Document, keyForId.Result);
                            dependencies.Add(new ArtifactDependency(guidUdi, false, ArtifactDependencyMode.Exist));
                        }
                    }
                    link["id"] = guidUdi?.ToString();
                }
                else
                {
                    var id = 0;
                    if (GuidUdi.TryParse(link.Value<string>("id"), out GuidUdi guidUdi))
                    {
                        var idForUdi = _entityService.GetIdForUdi(guidUdi);
                        if (idForUdi.Success)
                        {
                            id = idForUdi.Result;
                        }
                    }
                    link["id"] = id;
                }

                // Parse children
                var children = link.Value<JArray>("children");
                if (children != null)
                {
                    link["children"] = ParseLinks(children, dependencies, direction);
                }
            }

            return links;
        }
    }
}
