﻿// ======================================================================================
//  File:		CustomizationComparer.cs
//  Summary:	CustomizationComparer compares two customization files.
// ======================================================================================
//
//  This file is part of the Microsoft CRM 4.0 SDK Code Samples.
//
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//
//  This source code is intended only as a supplement to Microsoft
//  Development Tools and/or on-line documentation.  See these other
//  materials for detailed information regarding Microsoft code samples.
//
//  THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
//  KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
//  IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
//  PARTICULAR PURPOSE.
//
// =======================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Crm.Isv.Customizations;
using System.Globalization;
using Ionic.Zip;

namespace Microsoft.Crm.Isv
{
    /// <summary>
    /// CustomizationComparer compares two customization files.
    /// </summary>
    public class CustomizationComparer
    {
        /// <summary>
        /// Compares the specified customization files.
        /// </summary>
        /// <param name="sourcePath">The source customization path.</param>
        /// <param name="targetPath">The target customization path.</param>
        /// <returns></returns>
        public CustomizationComparison Compare(string sourcePath, string targetPath)
        {
            ImportExportXml sourceImportExport = LoadImportExportObject(sourcePath);
            ImportExportXml targetImportExport = LoadImportExportObject(targetPath);
            CustomizationComparison ret = new CustomizationComparison("Import Export Xml", sourceImportExport, targetImportExport);
            BuildComparisons(ret, null, sourceImportExport, targetImportExport);

            return ret;
        }

        private static Stream GetCustomizationStream(string path)
        {
            try 
            {

                Stream fileStream = File.OpenRead(path);

                if (Path.GetExtension(path).ToUpperInvariant() == ".ZIP")
             {
                using (ZipFile zip = ZipFile.Read(fileStream))
                {
                    Stream CustomStream = new MemoryStream();
                    ZipEntry e = zip["customizations.xml"];
                    //e.Extract("c:\downloads\\custom");
                    e.Extract(CustomStream);
                    CustomStream.Position = 0;
                    return CustomStream;
                   
                }
            }
            else
            {
                return fileStream;
            }
            }
        catch (InvalidOperationException ex)
            {
                throw ex;

        }
        }

        /// <summary>
        /// Gets the first zip entry.  This is just enough zip file support to 
        /// extract the customizations.xml file from a customizations.zip file
        /// generated by CRM.
        /// </summary>
        /// <param name="zipStream">The compressed zip stream.</param>
        /// <returns>A stream that can be read to extract the customization contents.</returns>
        private static Stream GetCustomizationZipEntry(Stream zipStream)
        {
            //Stream CustomizationStream;

            //using (ZipFile zip = ZipFile.Read(zipStream))
            //{
            //    ZipEntry e = zip["customizations.xml"];
            //    e.Extract(GetCustomizationStream);
            //}


            ////    if (version == 20 && compressionMethod == 8)
            ////    {
            ////        return new DeflateStream(zipStream, CompressionMode.Decompress);
            ////    }
            ////}


            return null;
        }

        private static ImportExportXml LoadImportExportObject(string path)
        {
            using (Stream stream = GetCustomizationStream(path))
            {
                if (stream == null)
                {
                    throw new InvalidDataException(String.Format(CultureInfo.CurrentCulture, "'{0}' is not a valid customizations file.", path));
                }
                else
                {
                    try
                    {
                        XmlSerializer s = new XmlSerializer(typeof(ImportExportXml));
                        return (ImportExportXml)s.Deserialize(stream);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidDataException(String.Format(CultureInfo.CurrentCulture, "'{0}' is not a valid customizations file.", path), e);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively loops through the customization object hierarchy and creates a CustomizationComparison hierarchy.
        /// </summary>
        /// <param name="parent">The parent CustomizationComparison.</param>
        /// <param name="prop">The property that source and target are values of.</param>
        /// <param name="source">The source value.</param>
        /// <param name="target">The target value.</param>
        private void BuildComparisons(CustomizationComparison parent, PropertyInfo prop, object source, object target)
        {
            // Make sure at least one value is not null
            if (source != null || target != null)
            {
                // Extract the value types
                Type type = GetCommonType(source, target);

                // Don't continue if the types differ
                if (type == null)
                {
                    parent.IsDifferent = true;
                }
                else
                {
                    CustomizationComparison originalParent = parent;

                    // Determine if a new CustomizationComparison node should be created
                    if (type != typeof(ImportExportXml) && ComparisonTypeMap.IsTypeComparisonType(type))
                    {
                        string name = ComparisonTypeMap.GetComparisonTypeName(source, target);

                        parent = new CustomizationComparison(name, source, target);
                        parent.ParentProperty = prop;
                        originalParent.Children.Add(parent);
                    }

                    if (IsSimpleType(type))
                    {
                        // for simple types just compare values
                        if (!Object.Equals(source, target))
                        {
                            originalParent.IsDifferent = true;
                            parent.IsDifferent = true;
                        }
                    }
                    else if (typeof(IEnumerable).IsAssignableFrom(type))
                    {
                        // Several arrays need to be sorted by a specific property (for example: Entity name)
                        originalParent.IsDifferent |= BuildArrayComparisonTypes(parent, prop, (IEnumerable)source, (IEnumerable)target);
                    }
                    else
                    {
                        // for classes, just compare each property
                        foreach (PropertyInfo p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (p.CanRead)
                            {
                                object sourceValue = source != null ? p.GetValue(source, null) : null;
                                object targetValue = target != null ? p.GetValue(target, null) : null;

                                BuildComparisons(parent, p, sourceValue, targetValue);
                                originalParent.IsDifferent |= parent.IsDifferent;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Builds the comparison types for arrays.
        /// </summary>
        /// <param name="parent">The parent CustomizationComparison.</param>
        /// <param name="prop">The property that source and target are values of.</param>
        /// <param name="source">The source value.</param>
        /// <param name="target">The target value.</param>
        /// <returns>true if the arrays are different; false if equal</returns>
        private bool BuildArrayComparisonTypes(CustomizationComparison parent, PropertyInfo prop, IEnumerable source, IEnumerable target)
        {
            bool isDifferent = false;

            Array sourceArray = ToArray(source);
            Array targetArray = ToArray(target);

            // If the arrays need special sorting, this will sort them and 
            // insert nulls for the missing entries from either the source 
            // or target.
            SynchronizeArraysByIdentity(ref sourceArray, ref targetArray);

            int sourceLength = sourceArray == null ? 0 : sourceArray.Length;
            int targetLength = targetArray == null ? 0 : targetArray.Length;

            for (int i = 0; i < Math.Max(sourceLength, targetLength); i++)
            {
                object sourceItem = i < sourceLength ? sourceArray.GetValue(i) : null;
                object targetItem = i < targetLength ? targetArray.GetValue(i) : null;

                BuildComparisons(parent, prop, sourceItem, targetItem);
                isDifferent |= parent.IsDifferent;
            }

            return isDifferent;
        }

        private static Array ToArray(IEnumerable enumerable)
        {
            if(enumerable == null) return null;
            if(enumerable.GetType().IsArray) return (Array)enumerable;
            return enumerable.Cast<Object>().ToArray();
        }

        /// <summary>
        /// Gets the common type of source and the target objects.
        /// </summary>
        /// <param name="source">The source object.</param>
        /// <param name="target">The target object.</param>
        /// <returns>The common type if source and target are of the same type or one of them is null; otherwise null</returns>
        private static Type GetCommonType(object source, object target)
        {
            Type type = null;

            if (source != null && target != null)
            {
                if (source.GetType() == target.GetType())
                {
                    type = source.GetType();
                }
            }
            else
            {
                type = (source ?? target).GetType();
            }

            return type;
        }

        private static bool IsSimpleType(Type type)
        {
            return (!type.IsClass || type == typeof(String));
        }

        /// <summary>
        /// Synchronizes the arrays by identity.
        /// </summary>
        /// <param name="source">The source array.</param>
        /// <param name="target">The target array.</param>
        private static void SynchronizeArraysByIdentity(ref Array source, ref Array target)
        {
            if (source == null && target == null) return;
            
            // Only proceed if the array elements implement IIdentifiable.
            Type elementType = (source ?? target).GetType().GetElementType();
            if (!typeof(IIdentifiable).IsAssignableFrom(elementType)) return;

            IIdentifiable[] sourceIdentities = source == null ? new IIdentifiable[0] :
                source.Cast<IIdentifiable>().ToArray();

            IIdentifiable[] targetIdentities = target == null ? new IIdentifiable[0] :
                target.Cast<IIdentifiable>().ToArray();

            // create a list of combined entities to determine the order by 
            // which both lists will be sorted
            string[] combinedIdentities = sourceIdentities.Select(i => i.Identity)
                .Union(targetIdentities.Select(i => i.Identity))
                .OrderBy(s => s)
                .ToArray();

            source = SortAndPad(combinedIdentities, sourceIdentities);
            target = SortAndPad(combinedIdentities, targetIdentities);
        }

        /// <summary>
        /// Sorts an array based on a set of keys and fills in missing key values with null.
        /// </summary>
        /// <param name="identityKeys">The identity keys.</param>
        /// <param name="array">The array.</param>
        /// <returns>The sorted array.</returns>
        private static Array SortAndPad(string[] identityKeys, IIdentifiable[] array)
        {
            if (array == null) return null;
            
            return identityKeys
                .Select(k => array.FirstOrDefault(i => i.Identity == k))
                .ToArray();
        }
    }
}