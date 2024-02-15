﻿using Umbraco.Core.PropertyEditors;

namespace Umbraco.Deploy.Contrib.Migrators.Legacy
{
    /// <summary>
    /// Migrates the <see cref="DataTypeArtifact" /> to replace the <see cref="FromEditorAlias" /> editor with <see cref="Constants.PropertyEditors.Aliases.MediaPicker" /> and the configuration from Umbraco 7 to <see cref="MediaPickerConfiguration" />.
    /// </summary>
    public class MultipleMediaPickerDataTypeArtifactMigrator : MediaPickerReplaceDataTypeArtifactMigratorBase
    {
        private const string FromEditorAlias = "Umbraco.MultipleMediaPicker";

        /// <inheritdoc />
        protected override bool Multiple => true;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultipleMediaPickerDataTypeArtifactMigrator" /> class.
        /// </summary>
        /// <param name="propertyEditors">The property editors.</param>
        public MultipleMediaPickerDataTypeArtifactMigrator(PropertyEditorCollection propertyEditors)
            : base(FromEditorAlias, propertyEditors)
        { }
    }
}
