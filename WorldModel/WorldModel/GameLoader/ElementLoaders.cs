﻿using System;
using System.Xml;
using System.Linq;
using AxeSoftware.Quest.Scripts;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AxeSoftware.Quest
{
    partial class GameLoader
    {
        private delegate void LoadNestedXMLHandler(XmlReader newReader);

        private Dictionary<string, IXMLLoader> m_xmlLoaders = new Dictionary<string, IXMLLoader>();
        private IXMLLoader m_defaultXmlLoader;

        private void AddXMLLoaders(LoadMode mode)
        {
            // Use Reflection to create instances of all IXMLLoaders
            foreach (Type t in AxeSoftware.Utility.Classes.GetImplementations(System.Reflection.Assembly.GetExecutingAssembly(),
                typeof(IXMLLoader)))
            {
                AddXMLLoader((IXMLLoader)Activator.CreateInstance(t), mode);
            }

            m_defaultXmlLoader = new DefaultXMLLoader();
            InitXMLLoader(m_defaultXmlLoader);
        }

        private Dictionary<string, IAttributeLoader> AttributeLoaders
        {
            get { return m_attributeLoaders; }
        }

        private Dictionary<string, IExtendedAttributeLoader> ExtendedAttributeLoaders
        {
            get { return m_extendedAttributeLoaders; }
        }

        private WorldModel WorldModel
        {
            get { return m_worldModel; }
        }

        private ScriptFactory ScriptFactory
        {
            get { return m_scriptFactory; }
        }

        private void AddXMLLoader(IXMLLoader loader, LoadMode mode)
        {
            InitXMLLoader(loader);
            if (loader.AppliesTo != null && loader.SupportsMode(mode))
            {
                m_xmlLoaders.Add(loader.AppliesTo, loader);
            }
        }

        private void InitXMLLoader(IXMLLoader loader)
        {
            loader.GameLoader = this;
            loader.AddError += AddError;
            loader.LoadNestedXML += LoadXML;
        }

        private interface IXMLLoader
        {
            event AddErrorHandler AddError;
            event LoadNestedXMLHandler LoadNestedXML;
            string AppliesTo { get; }
            void StartElement(XmlReader reader, ref Element current);
            void EndElement(XmlReader reader, ref Element current);
            void SetText(string text, ref Element current);
            GameLoader GameLoader { set; }
            bool SupportsMode(LoadMode mode);
        }

        private abstract class XMLLoaderBase : IXMLLoader
        {
            private WorldModel m_worldModel = null;
            private GameLoader m_loader = null;

            protected WorldModel WorldModel
            {
                get { return m_worldModel; }
            }

            #region IXMLLoader Members

            public event AddErrorHandler AddError;
            public event LoadNestedXMLHandler LoadNestedXML;
            public abstract string AppliesTo { get; }
            public void StartElement(XmlReader reader, ref Element current)
            {
                object createdObject = Load(reader, ref current);

                if (createdObject != null && createdObject is Element)
                {
                    GameLoader.AddedElement((Element)createdObject);
                }

                if (CanContainNestedAttributes)
                {
                    if (createdObject != null && !reader.IsEmptyElement) current = (Element)createdObject;
                }
            }

            public void EndElement(XmlReader reader, ref Element current)
            {
                if (!CanContainNestedAttributes) return;

                if (current.Parent == null)
                {
                    current = null;
                }
                else
                {
                    current = current.Parent;
                }
            }

            public GameLoader GameLoader
            {
                set
                {
                    m_loader = value;
                    m_worldModel = m_loader.WorldModel;
                }
                get { return m_loader; }
            }

            public virtual void SetText(string text, ref Element current)
            {
            }

            public virtual bool SupportsMode(LoadMode mode)
            {
                return true;
            }
            #endregion

            public abstract object Load(XmlReader reader, ref Element current);

            protected virtual bool CanContainNestedAttributes
            {
                get { return false; }
            }

            protected void RaiseError(string error)
            {
                AddError(error);
            }

            protected void LoadXML(XmlReader newReader)
            {
                LoadNestedXML(newReader);
            }
        }

        private class CommandLoader : XMLLoaderBase
        {
            private RequiredAttributes m_requiredAttributes = new RequiredAttributes(
                new RequiredAttribute("name", true, true, false),
                new RequiredAttribute("pattern", true, true, false),
                new RequiredAttribute("unresolved", true, true, false));

            public override string AppliesTo
            {
                get { return "command"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return Load(reader, ref current, null);
            }

            protected object Load(XmlReader reader, ref Element current, string defaultName)
            {
                Dictionary<string, string> data = GameLoader.GetRequiredAttributes(reader, m_requiredAttributes);

                string pattern = data["pattern"];
                string name = data["name"];

                bool anonymous = false;

                if (string.IsNullOrEmpty(name)) name = defaultName;
                if (string.IsNullOrEmpty(name))
                {
                    anonymous = true;
                    name = GetUniqueCommandID(pattern);
                }

                Element newCommand = WorldModel.ObjectFactory.CreateCommand(name);

                if (anonymous)
                {
                    newCommand.Fields[FieldDefinitions.Anonymous] = true;
                }

                if (current != null)
                {
                    newCommand.Parent = (Element)current;
                }

                newCommand.Fields[FieldDefinitions.Pattern] = pattern;

                string unresolved = data["unresolved"];
                if (!string.IsNullOrEmpty(unresolved)) newCommand.Fields[FieldDefinitions.Unresolved] = unresolved;

                return newCommand;
            }

            public override void SetText(string text, ref Element current)
            {
                current.Fields.LazyFields.AddScript("script", GameLoader.GetTemplate(text));
            }

            protected override bool CanContainNestedAttributes
            {
                get
                {
                    return true;
                }
            }

            private Regex m_regex = new Regex("[A-Za-z0-9]+");

            private string GetUniqueCommandID(string pattern)
            {
                string name = pattern == null ? null : m_regex.Match(pattern.Replace(" ", "")).Value;

                if (string.IsNullOrEmpty(name) || WorldModel.ObjectExists(name)) name = WorldModel.GetUniqueID(name);
                return name;
            }
        }

        private class VerbLoader : CommandLoader
        {
            public override string AppliesTo
            {
                get { return "verb"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                string property = reader.GetAttribute("property");

                Element newCommand = (Element)base.Load(reader, ref current, property);

                newCommand.Fields[FieldDefinitions.Property] = property;

                string response = reader.GetAttribute("response");
                if (!string.IsNullOrEmpty(response))
                {
                    newCommand.Fields[FieldDefinitions.DefaultTemplate] = response;
                }

                newCommand.Fields.LazyFields.AddType("defaultverb");
                newCommand.Fields[FieldDefinitions.IsVerb] = true;

                return newCommand;
            }

            public override void SetText(string text, ref Element current)
            {
                string contents = GameLoader.GetTemplate(text);
                current.Fields[FieldDefinitions.DefaultText] = contents;
            }
        }

        private abstract class IncludeLoaderBase : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "include"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                string file = GameLoader.GetTemplateAttribute(reader, "ref");
                string path = WorldModel.GetExternalPath(file);
                XmlReader newReader = XmlReader.Create(path);
                newReader.Read();
                if (newReader.Name != "library")
                {
                    RaiseError(string.Format("Included file '{0}' is not a library", file));
                }
                LoadXML(newReader);
                return LoadInternal(file);
            }

            protected abstract object LoadInternal(string file);
            protected abstract LoadMode LoaderMode { get; }

            public override bool SupportsMode(LoadMode mode)
            {
                return mode == LoaderMode;
            }
        }

        private class IncludeLoader : IncludeLoaderBase
        {
            protected override object LoadInternal(string file)
            {
                return null;
            }

            protected override LoadMode LoaderMode
            {
                get { return LoadMode.Play; }
            }
        }

        private class EditorIncludeLoader : IncludeLoaderBase
        {
            protected override object LoadInternal(string file)
            {
                Element include = WorldModel.GetElementFactory(ElementType.IncludedLibrary).Create();
                include.Fields[FieldDefinitions.Filename] = file;
                include.Fields[FieldDefinitions.Anonymous] = true;
                return include;
            }

            protected override LoadMode LoaderMode
            {
                get { return LoadMode.Edit; }
            }
        }

        private class LibraryLoader : XMLLoaderBase
        {
            // This class doesn't need to do anything. The initial <library> tag is loaded from the XML
            // by the IncludeLoader, but we still need something to handle the closing tag.

            public override string AppliesTo
            {
                get { return "library"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return null;
            }
        }

        private class ASLElementLoader : XMLLoaderBase
        {
            // This class doesn't need to do anything, it just handles the closing </asl> tag.

            public override string AppliesTo
            {
                get { return "asl"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return null;
            }
        }

        private class DefaultXMLLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return null; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                string attribute = reader.Name;
                string type = reader.GetAttribute("type");

                if (type == null)
                {
                    string currentElementType = current.Fields.GetString("type");
                    if (string.IsNullOrEmpty(currentElementType))
                    {
                        // the type property is the object type, so is not set for other element types.
                        currentElementType = current.Fields.GetString("elementtype");
                    }
                    type = GameLoader.m_implicitTypes.Get(currentElementType, attribute);
                }

                if (type != null && GameLoader.ExtendedAttributeLoaders.ContainsKey(type))
                {
                    GameLoader.ExtendedAttributeLoaders[type].Load(reader, current);
                }
                else
                {
                    string value;

                    try
                    {
                        value = GameLoader.GetTemplateContents(reader);
                    }
                    catch (XmlException)
                    {
                        RaiseError(string.Format("Error loading XML data for '{0}.{1}' - ensure that it contains no nested XML", current.Name, attribute));
                        return null;
                    }

                    if (type == null)
                    {
                        if (value.Length > 0)
                        {
                            type = "string";
                        }
                        else
                        {
                            type = "boolean";
                        }
                    }

                    if (GameLoader.AttributeLoaders.ContainsKey(type))
                    {
                        GameLoader.AttributeLoaders[type].Load(current, attribute, value);
                    }
                    else
                    {
                        Element del;
                        if (WorldModel.Elements.TryGetValue(ElementType.Delegate, type, out del))
                        {
                            Element proc = WorldModel.GetElementFactory(ElementType.Delegate).Create();
                            // TO DO: These should be lazy loaded, so we can refer to other procedures that are defined later in the XML
                            proc.MetaFields[MetaFieldDefinitions.DelegateImplementation] = true;
                            proc.Fields[FieldDefinitions.Script] = GameLoader.ScriptFactory.CreateScript(value, proc);
                            current.Fields.Set(attribute, new DelegateImplementation(type, del, proc));
                        }
                        else
                        {
                            RaiseError(string.Format("Unrecognised attribute type '{0}' in '{1}.{2}'", type, current.Name, attribute));
                        }
                    }
                }
                return null;
            }
        }

        private class GameElementLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "game"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                string name = reader.GetAttribute("name");
                WorldModel.SetGameName(name);
                WorldModel.Game.Fields[FieldDefinitions.GameName] = name;
                return WorldModel.Game;
            }

            protected override bool CanContainNestedAttributes { get { return true; } }
        }

        // TO DO: This should derive from ElementLoaderBase
        private class ObjectLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "object"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                Element newElement;

                if (current != null)
                {
                    newElement = WorldModel.ObjectFactory.CreateObject(reader.GetAttribute("name"), (Element)current);
                }
                else
                {
                    newElement = WorldModel.ObjectFactory.CreateObject(reader.GetAttribute("name"));
                }

                return newElement;
            }

            protected override bool CanContainNestedAttributes { get { return true; } }
        }

        private class ExitLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "exit"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                string alias = reader.GetAttribute("alias");
                string to = reader.GetAttribute("to");
                string id = reader.GetAttribute("name");
                Element newElement;
                if (string.IsNullOrEmpty(alias) && !WorldModel.EditMode)
                {
                    alias = to;
                }
                if (string.IsNullOrEmpty(id))
                {
                    newElement = WorldModel.ObjectFactory.CreateExitLazy(alias, (Element)current, to);
                }
                else
                {
                    newElement = WorldModel.ObjectFactory.CreateExitLazy(id, alias, (Element)current, to);
                }

                return newElement;
            }

            protected override bool CanContainNestedAttributes { get { return true; } }
        }

        private class TypeLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "type"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                Element newElement = WorldModel.GetElementFactory(ElementType.ObjectType).Create(reader.GetAttribute("name"));
                return newElement;
            }

            protected override bool CanContainNestedAttributes { get { return true; } }
        }

        private class TemplateLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "template"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                bool isCommandTemplate = (reader.GetAttribute("templatetype") == "command");
                return AddTemplate(reader.GetAttribute("name"), reader.ReadElementContentAsString(), isCommandTemplate);
            }

            private Element AddTemplate(string t, string text, bool isCommandTemplate)
            {
                if (string.IsNullOrEmpty(t))
                {
                    RaiseError("Expected 'name' attribute in template");
                    return null;
                }
                return WorldModel.Template.AddTemplate(t, text, isCommandTemplate);
            }
        }

        private class VerbTemplateLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "verbtemplate"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return AddVerbTemplate(reader.GetAttribute("name"), reader.ReadElementContentAsString());
            }

            private Element AddVerbTemplate(string c, string text)
            {
                return WorldModel.Template.AddVerbTemplate(c, text);
            }
        }

        // TO DO: Use RequiredAttributes in the above XML loaders too...

        private class DynamicTemplateLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "dynamictemplate"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return AddDynamicTemplate(reader.GetAttribute("name"), reader.ReadElementContentAsString());
            }

            private Element AddDynamicTemplate(string t, string expression)
            {
                if (string.IsNullOrEmpty(t))
                {
                    RaiseError("Expected 'name' attribute in template");
                    return null;
                }
                return WorldModel.Template.AddDynamicTemplate(t, expression);
            }
        }

        private abstract class FunctionLoaderBase : XMLLoaderBase
        {
            private RequiredAttributes m_required = new RequiredAttributes(
                new RequiredAttribute("name"),
                new RequiredAttribute(false, "parameters"),
                new RequiredAttribute(false, "type"));

            private string[] m_delimiters = new string[] { ", ", "," };

            protected RequiredAttributes RequiredAttributes
            {
                get { return m_required; }
            }

            protected void SetupProcedure(Element proc, string returnType, string script, string name, string parameters)
            {
                string[] paramNames = null;

                if (!string.IsNullOrEmpty(parameters))
                {
                    paramNames = parameters.Split(m_delimiters, StringSplitOptions.None);
                }

                Type returns = null;
                if (!string.IsNullOrEmpty(returnType))
                {
                    try
                    {
                        returns = WorldModel.ConvertTypeNameToType(returnType);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        RaiseError(string.Format("Unrecognised function return type '{0}' in '{1}'", returnType, name));
                    }
                }

                proc.Fields[FieldDefinitions.ParamNames] = new QuestList<string>(paramNames);
                if (returns != null)
                {
                    proc.Fields[FieldDefinitions.ReturnType] = WorldModel.ConvertTypeToTypeName(returns);
                }

                // TO DO: These should be lazy loaded, so we can refer to other procedures that are defined later in the XML
                proc.Fields[FieldDefinitions.Script] = GameLoader.ScriptFactory.CreateScript(script, proc);
            }
        }

        private class FunctionLoader : FunctionLoaderBase
        {
            public override string AppliesTo
            {
                get { return "function"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return AddProcedure(reader);
            }

            private Element AddProcedure(XmlReader reader)
            {
                Dictionary<string, string> data = GameLoader.GetRequiredAttributes(reader, RequiredAttributes);
                Element proc = WorldModel.AddProcedure(data["name"]);
                SetupProcedure(proc, data["type"], GameLoader.GetTemplateContents(reader), data["name"], data["parameters"]);
                return proc;
            }
        }

        private class InheritLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "inherit"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                current.Fields.LazyFields.AddType(reader.GetAttribute("name"));
                return null;
            }
        }

        private class WalkthroughLoader : ElementLoaderBase
        {
            public override string AppliesTo
            {
                get { return "walkthrough"; }
            }

            public override ElementType CreateElementType
            {
                get { return ElementType.Walkthrough; }
            }

            protected override string IDPrefix
            {
                get { return "walkthrough"; }
            }
        }

        private class DelegateLoader : FunctionLoaderBase
        {
            public override string AppliesTo
            {
                get { return "delegate"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                return AddDelegate(reader);
            }

            private Element AddDelegate(XmlReader reader)
            {
                Dictionary<string, string> data = GameLoader.GetRequiredAttributes(reader, RequiredAttributes);
                Element del = WorldModel.AddDelegate(data["name"]);
                SetupProcedure(del, data["type"], GameLoader.GetTemplateContents(reader), data["name"], data["parameters"]);
                return del;
            }
        }

        private abstract class ImpliedTypeLoaderBase : XMLLoaderBase
        {
            private RequiredAttributes m_required = new RequiredAttributes(false, "element", "property", "type");

            public override string AppliesTo
            {
                get { return "implied"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                Dictionary<string, string> data = GameLoader.GetRequiredAttributes(reader, m_required);
                GameLoader.m_implicitTypes.Add(data["element"], data["property"], data["type"]);
                return LoadInternal(data["element"], data["property"], data["type"]);
            }

            protected abstract object LoadInternal(string element, string property, string type);
            protected abstract LoadMode LoaderMode { get; }

            public override bool SupportsMode(LoadMode mode)
            {
                return mode == LoaderMode;
            }
        }

        private class ImpliedTypeLoader : ImpliedTypeLoaderBase
        {
            protected override object LoadInternal(string element, string property, string type)
            {
                return null;
            }

            protected override LoadMode LoaderMode
            {
                get { return LoadMode.Play; }
            }
        }

        private class EditorImpliedTypeLoader : ImpliedTypeLoaderBase
        {
            protected override object LoadInternal(string element, string property, string type)
            {
                Element e = WorldModel.GetElementFactory(ElementType.ImpliedType).Create();
                e.Fields[FieldDefinitions.Element] = element;
                e.Fields[FieldDefinitions.Property] = property;
                e.Fields[FieldDefinitions.Type] = type;
                return e;
            }

            protected override LoadMode LoaderMode
            {
                get { return LoadMode.Edit; }
            }
        }

        private abstract class ElementLoaderBase : XMLLoaderBase
        {
            public override object Load(XmlReader reader, ref Element current)
            {
                string name = reader.GetAttribute("name");
                if (string.IsNullOrEmpty(name)) name = WorldModel.GetUniqueID(IDPrefix);
                Element newElement = WorldModel.GetElementFactory(CreateElementType).Create(name);
                newElement.Parent = current;
                return newElement;
            }

            protected override bool CanContainNestedAttributes { get { return true; } }

            public abstract ElementType CreateElementType { get; }

            protected abstract string IDPrefix { get; }
        }

        private class EditorLoader : ElementLoaderBase
        {
            public override string AppliesTo
            {
                get { return "editor"; }
            }

            public override ElementType CreateElementType
            {
                get { return ElementType.Editor; }
            }

            protected override string IDPrefix
            {
                get { return "editor"; }
            }
        }

        private class EditorTabLoader : ElementLoaderBase
        {
            public override string AppliesTo
            {
                get { return "tab"; }
            }

            public override ElementType CreateElementType
            {
                get { return ElementType.EditorTab; }
            }

            protected override string IDPrefix
            {
                get { return "editor"; }
            }
        }

        private class EditorControlLoader : ElementLoaderBase
        {
            public override string AppliesTo
            {
                get { return "control"; }
            }

            public override ElementType CreateElementType
            {
                get { return ElementType.EditorControl; }
            }

            protected override string IDPrefix
            {
                get { return "editor"; }
            }
        }

        // TO DO: Will need different loader for the Editor as we don't want to resolve the file path

        private class JavascriptReferenceLoader : XMLLoaderBase
        {
            public override string AppliesTo
            {
                get { return "javascript"; }
            }

            public override object Load(XmlReader reader, ref Element current)
            {
                Element jsRef = WorldModel.GetElementFactory(ElementType.Javascript).Create();
                string file = GameLoader.GetTemplateAttribute(reader, "src");
                string path = WorldModel.GetExternalPath(file);
                jsRef.Fields[FieldDefinitions.Src] = path;

                return jsRef;
            }
        }
    }
}
