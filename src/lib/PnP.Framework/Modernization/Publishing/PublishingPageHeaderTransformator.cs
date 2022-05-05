﻿using Microsoft.SharePoint.Client;
using PnP.Framework.Modernization.Telemetry;
using PnP.Framework.Modernization.Transform;
using System;
using System.Collections.Generic;
using System.Linq;
using PnPCore = PnP.Core.Model.SharePoint;

namespace PnP.Framework.Modernization.Publishing
{
    /// <summary>
    /// Class that will handle the configuration of the modern page header when transforming publishing pages
    /// </summary>
    public class PublishingPageHeaderTransformator: BaseTransform
    {
        private PublishingPageTransformationInformation publishingPageTransformationInformation;
        private PublishingPageTransformation publishingPageTransformation;
        private PublishingFunctionProcessor functionProcessor;
        private ClientContext sourceClientContext;
        private ClientContext targetClientContext;

        #region Construction
        public PublishingPageHeaderTransformator(PublishingPageTransformationInformation publishingPageTransformationInformation, ClientContext sourceClientContext, ClientContext targetClientContext, PublishingPageTransformation publishingPageTransformation, IList<ILogObserver> logObservers = null)
        {
            // Register observers
            if (logObservers != null)
            {
                foreach (var observer in logObservers)
                {
                    base.RegisterObserver(observer);
                }
            }

            this.publishingPageTransformationInformation = publishingPageTransformationInformation;
            this.publishingPageTransformation = publishingPageTransformation;
            this.sourceClientContext = sourceClientContext;
            this.targetClientContext = targetClientContext;
            this.functionProcessor = new PublishingFunctionProcessor(publishingPageTransformationInformation.SourcePage, sourceClientContext, targetClientContext, this.publishingPageTransformation, publishingPageTransformationInformation as BaseTransformationInformation, base.RegisteredLogObservers);
        }
        #endregion

        #region Header transformation
        /// <summary>
        /// Builds the header for the modern page
        /// </summary>
        /// <param name="targetPage">Modern page instance</param>
        public void TransformHeader(ref PnPCore.IPage targetPage)
        {
            // Get the mapping model to use as it describes how the page header needs to be generated
            var publishingPageTransformationModel = new PageLayoutManager(this.RegisteredLogObservers).GetPageLayoutMappingModel(this.publishingPageTransformation, publishingPageTransformationInformation.SourcePage);

            // Configure the page header
            if (publishingPageTransformationModel.PageHeader == PageLayoutPageHeader.None)
            {
                targetPage.RemovePageHeader();
            }
            else if (publishingPageTransformationModel.PageHeader == PageLayoutPageHeader.Default)
            {
                targetPage.SetDefaultPageHeader();
            }
            else
            {
                // Custom page header

                // ImageServerRelativeUrl 
                string imageServerRelativeUrl = "";
                HeaderField imageServerRelativeUrlField = GetHeaderField(publishingPageTransformationModel, HeaderFieldHeaderProperty.ImageServerRelativeUrl);

                if (imageServerRelativeUrlField != null)
                {
                    imageServerRelativeUrl = GetFieldValue(imageServerRelativeUrlField);
                }

                bool headerCreated = false;
                // Did we get a header image url?
                if (!string.IsNullOrEmpty(imageServerRelativeUrl))
                {
                    string newHeaderImageServerRelativeUrl = "";
                    try
                    {
                        // Integrate asset transformator
                        AssetTransfer assetTransfer = new AssetTransfer(this.sourceClientContext, this.targetClientContext, base.RegisteredLogObservers);
                        newHeaderImageServerRelativeUrl = assetTransfer.TransferAsset(imageServerRelativeUrl, System.IO.Path.GetFileNameWithoutExtension(publishingPageTransformationInformation.SourcePage[Constants.FileLeafRefField].ToString()));
                    }
                    catch (Exception ex)
                    {
                        LogError(LogStrings.Error_HeaderImageAssetTransferFailed, LogStrings.Heading_PublishingPageHeader, ex);
                    }

                    if (!string.IsNullOrEmpty(newHeaderImageServerRelativeUrl))
                    {
                        LogInfo(string.Format(LogStrings.SettingHeaderImage, newHeaderImageServerRelativeUrl), LogStrings.Heading_PublishingPageHeader);
                        targetPage.SetCustomPageHeader(newHeaderImageServerRelativeUrl);
                        headerCreated = true;
                    }
                }

                if (!headerCreated)
                {
                    // let's fall back to the default header
                    targetPage.SetDefaultPageHeader();
                }

                // Header type handling
                switch (publishingPageTransformationModel.Header.Type)
                {
                    case HeaderType.ColorBlock: targetPage.PageHeader.LayoutType = PnPCore.PageHeaderLayoutType.ColorBlock; break;
                    case HeaderType.CutInShape: targetPage.PageHeader.LayoutType = PnPCore.PageHeaderLayoutType.CutInShape; break;
                    case HeaderType.NoImage: targetPage.PageHeader.LayoutType = PnPCore.PageHeaderLayoutType.NoImage; break;
                    case HeaderType.FullWidthImage: targetPage.PageHeader.LayoutType = PnPCore.PageHeaderLayoutType.FullWidthImage; break;
                }

                // Alignment handling
                switch (publishingPageTransformationModel.Header.Alignment)
                {
                    case HeaderAlignment.Left: targetPage.PageHeader.TextAlignment = PnPCore.PageHeaderTitleAlignment.Left; break;
                    case HeaderAlignment.Center: targetPage.PageHeader.TextAlignment = PnPCore.PageHeaderTitleAlignment.Center; break;
                }

                // Show published date
                targetPage.PageHeader.ShowPublishDate = publishingPageTransformationModel.Header.ShowPublishedDate;

                // Topic header handling
                HeaderField topicHeaderField = GetHeaderField(publishingPageTransformationModel, HeaderFieldHeaderProperty.TopicHeader);
                if (topicHeaderField != null)
                {
                    if (publishingPageTransformationInformation.SourcePage.FieldExistsAndUsed(topicHeaderField.Name))
                    {
                        var topicHeader = GetFieldValue(topicHeaderField);
                        targetPage.PageHeader.TopicHeader = topicHeader;
                        targetPage.PageHeader.ShowTopicHeader = true;
                    }
                }

                // AlternativeText handling
                HeaderField alternativeTextHeaderField = GetHeaderField(publishingPageTransformationModel, HeaderFieldHeaderProperty.AlternativeText);
                if (alternativeTextHeaderField != null)
                {
                    var alternativeTextHeader = GetFieldValue(alternativeTextHeaderField);
                    if (!string.IsNullOrEmpty(alternativeTextHeader))
                    {
                        targetPage.PageHeader.AlternativeText = alternativeTextHeader;                        
                    }
                }

                // Authors handling
                HeaderField authorsHeaderField = GetHeaderField(publishingPageTransformationModel, HeaderFieldHeaderProperty.Authors);
                if (authorsHeaderField != null)
                {
                    var authorsHeader = GetFieldValue(authorsHeaderField, PublishingFunctionProcessor.FieldType.User);
                    if (!string.IsNullOrEmpty(authorsHeader))
                    {
                        targetPage.PageHeader.Authors = authorsHeader;
                    }
                }
                
            }
        }

        #region Helper methods
        private string GetFieldValue(HeaderField headerField, PublishingFunctionProcessor.FieldType fieldType = PublishingFunctionProcessor.FieldType.String)
        {
            // check if the target field name contains a delimiter value
            if (headerField.Name.Contains(";"))
            {
                // extract the array of field names to process, and trims each one
                string[] targetFieldNames = headerField.Name.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

                // sets the field name to the first "valid" entry
                headerField.Name = this.publishingPageTransformationInformation.GetFirstNonEmptyFieldName(targetFieldNames);
            }

            if (!string.IsNullOrEmpty(headerField.Name))
            {
                string fieldValue = null;
                if (!string.IsNullOrEmpty(headerField.Functions))
                {
                    // execute function
                    var evaluatedField = this.functionProcessor.Process(headerField.Functions, headerField.Name, fieldType);
                    if (!string.IsNullOrEmpty(evaluatedField.Item1))
                    {
                        fieldValue = evaluatedField.Item2;
                    }
                }
                else
                {
                    fieldValue = this.publishingPageTransformationInformation.SourcePage.FieldValues[headerField.Name]?.ToString().Trim();
                }

                return fieldValue;
            }
            else
            {
                return null;
            }
        }

        private static HeaderField GetHeaderField(PageLayout publishingPageTransformationModel, HeaderFieldHeaderProperty fieldName)
        {
            return publishingPageTransformationModel.Header.Field.Where(p => p.HeaderProperty == fieldName).FirstOrDefault();
        }
        #endregion
        #endregion

    }
}
