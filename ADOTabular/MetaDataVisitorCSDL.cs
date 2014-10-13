﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Xml;
using ADOTabular.AdomdClientWrappers;

namespace ADOTabular
{
    public class MetaDataVisitorCSDL : IMetaDataVisitor
    {
        private readonly ADOTabularConnection _conn;

        public MetaDataVisitorCSDL(ADOTabularConnection conn)
        {
            _conn = conn;
        }
        
        SortedDictionary<string, ADOTabularModel> IMetaDataVisitor.Visit(ADOTabularModelCollection models)
        {
            var ret = new SortedDictionary<string, ADOTabularModel>();
            var resColl = new AdomdRestrictionCollection { { "CUBE_SOURCE", 1 } };
            var dtModels = _conn.GetSchemaDataSet("MDSCHEMA_CUBES", resColl).Tables[0];
            foreach (DataRow dr in dtModels.Rows)
            {
                ret.Add(dr["CUBE_NAME"].ToString()
                    , new ADOTabularModel(_conn, dr["CUBE_NAME"].ToString(), dr["DESCRIPTION"].ToString(), dr["BASE_CUBE_NAME"].ToString()));
            }
            return ret;
        }

        void IMetaDataVisitor.Visit(ADOTabularTableCollection tables)
        {
            var resColl = new AdomdRestrictionCollection { { "CATALOG_NAME", _conn.Database.Name } };
            // restrict the metadata to the selected perspective
            if (tables.Model.IsPerspective)
                resColl.Add(new AdomdRestriction("PERSPECTIVE_NAME",tables.Model.Name));
            // if we are SQL 2012 SP1 or greater ask for v1.1 of the Metadata (includes KPI & Hierarchy information)
            if (_conn.ServerVersion.VersionGreaterOrEqualTo("11.0.3000.0"))
                resColl.Add(new AdomdRestriction("VERSION", "1.1"));
            var ds = _conn.GetSchemaDataSet("DISCOVER_CSDL_METADATA", resColl);
            string csdl = ds.Tables[0].Rows[0]["Metadata"].ToString();

            /*
            //  debug code
            using (StreamWriter outfile = new StreamWriter( @"d:\data\csdl.xml"))
            {
                outfile.Write(csdl);
            }
            */

            using (XmlReader rdr = new XmlTextReader(new StringReader(csdl)))
            {
                GenerateTablesFromXmlReader(tables, rdr);
            }
        }

        public void GenerateTablesFromXmlReader(ADOTabularTableCollection tabs, XmlReader rdr)
        {
            if (rdr.NameTable != null)
            {
                var eEntitySet = rdr.NameTable.Add("EntitySet");
                var eEntityType = rdr.NameTable.Add("EntityType");
                var eProperty = rdr.NameTable.Add("Property");
                var eMeasure = rdr.NameTable.Add("Measure");
                var eSummary = rdr.NameTable.Add("Summary");
                var eKpi = rdr.NameTable.Add("Kpi");

                while (rdr.Read())
                {
                    if (rdr.NodeType == XmlNodeType.Element
                        && rdr.LocalName == eEntitySet)
                    {
                        var tab = BuildTableFromEntitySet(rdr, eEntitySet);
                        tabs.Add(tab);
                    }
                    if (rdr.NodeType == XmlNodeType.Element
                        && rdr.LocalName == eEntityType)
                    {
                        AddColumnsToTable(rdr, tabs, eEntityType, eProperty, eMeasure, eSummary);
                    }
                }
                foreach (var t in tabs)
                {
                    TagKpiComponentColumns(t);
                }
            }

        }

        

        private ADOTabularTable BuildTableFromEntitySet(XmlReader rdr, string eEntitySet)
        {
            string caption = "";
            string description = "";
            bool isVisible = true;
            string daxname = "";
            while (!(rdr.NodeType == XmlNodeType.EndElement
                     && rdr.LocalName == eEntitySet))
            {
                while (rdr.MoveToNextAttribute())
                {
                    switch (rdr.LocalName)
                    {
                        case "Caption":
                            caption = rdr.Value;
                            break;
                        case "Description":
                            description = rdr.Value;
                            break;
                        case "Hidden":
                            isVisible = !bool.Parse(rdr.Value);
                            break;
                        case "Name":
                            daxname = rdr.Value;
                            break;
                    }
                }
                rdr.Read();
            }
            if (caption.Length == 0)
                caption = daxname;
            var tab = new ADOTabularTable(_conn, caption, description, isVisible);
            tab.InternalId = daxname;
            return tab;
        }

        private void TagKpiComponentColumns(ADOTabularTable tab)
        {
            foreach( var c in tab.Columns )
            {
                if (c.ColumnType == ADOTabularColumnType.KPI)
                {
                    var k = (ADOTabularKpi)c;
                    k.Goal.ColumnType = ADOTabularColumnType.KPIGoal;
                    k.Status.ColumnType = ADOTabularColumnType.KPIStatus;
                }
            }
        }

        private void AddColumnsToTable(XmlReader rdr
            , ADOTabularTableCollection tables
            , string eEntityType
            , string eProperty
            , string eMeasure
            , string eSummary )
        {
            // this routine effectively processes and <EntityType> element and it's children
            string caption = "";
            string description = "";
            bool isVisible = true;
            string refName = "";
            string tableId = "";
            string dataType = "";
            string contents = "";
            KpiDetails kpi = new KpiDetails();

            var colType = ADOTabularColumnType.Column;
            while (!(rdr.NodeType == XmlNodeType.EndElement
                     && rdr.LocalName == eEntityType))
            {
                if (rdr.NodeType == XmlNodeType.Element 
                    && rdr.LocalName == eEntityType)
                {
                    while (rdr.MoveToNextAttribute())
                    {
                        switch (rdr.LocalName)
                        {
                            case "Name":
                                tableId = rdr.Value;
                                break;
                        }
                    }
                }

                if (rdr.NodeType == XmlNodeType.Element
                    && rdr.LocalName == "Hierarchy")
                {
                    ProcessHierarchy(rdr, tables.GetById(tableId),eEntityType);

                }


                if (rdr.NodeType == XmlNodeType.Element
                    && rdr.LocalName == "Kpi")
                {
                    kpi = ProcessKpi(rdr, tables.GetById(tableId));
                }

                if (rdr.NodeType == XmlNodeType.Element 
                    && (rdr.LocalName == eProperty 
                    || rdr.LocalName == eMeasure 
                    || rdr.LocalName == eSummary))
                {
                    
                    if (rdr.LocalName == eMeasure)
                        colType = ADOTabularColumnType.Measure;

                    if (rdr.LocalName == eSummary)
                        description = rdr.ReadElementContentAsString();

                    while (rdr.MoveToNextAttribute())
                    {
                        switch (rdr.LocalName)
                        {
                            case "Name":
                                refName = rdr.Value;
                                break;
                            case "Type":
                                dataType = rdr.Value;
                                break;
                            case "Caption":
                                caption = rdr.Value;
                                break;
                            case "Contents":
                                contents = rdr.Value;
                                break;
                            case "Hidden":
                                isVisible = !bool.Parse(rdr.Value);
                                break;
                            case "Description":
                                description = rdr.Value;
                                break;
                                // Precision Scale FormatString
                            //DefaultAggregateFunction
                        }
                    }

                }

                

                if (rdr.NodeType == XmlNodeType.EndElement 
                    && rdr.LocalName == eProperty 
                    && rdr.LocalName == "Property")
                {
                    
                    if (caption.Length == 0)
                        caption = refName;
                    if (!string.IsNullOrWhiteSpace(caption)) 
                    {
                        var tab = tables.GetById(tableId);
                        if (kpi.IsBlank())
                        {
                            var col = new ADOTabularColumn(tab, refName, caption, description, isVisible, colType, contents);
                            col.DataType = Type.GetType(string.Format("System.{0}", dataType));
                            tab.Columns.Add(col); 
                        }
                        else
                        {
                            colType = ADOTabularColumnType.KPI;
                            var kpiCol = new ADOTabularKpi(tab, refName, caption, description, isVisible, colType, contents,kpi);
                            kpiCol.DataType = Type.GetType(string.Format("System.{0}", dataType));
                            tab.Columns.Add(kpiCol); 
                        }
                    }
                    

                    // reset temp variables
                    kpi = new KpiDetails();
                    refName = "";
                    caption = "";
                    description = "";
                    isVisible = true;
                    contents = "";
                    dataType = "";
                    colType = ADOTabularColumnType.Column;
                }
                rdr.Read();
            }
            
            //TODO - link up back reference to backing measures for KPIs

        }


        private KpiDetails ProcessKpi(XmlReader rdr, ADOTabularTable table)
        {
            KpiDetails kpi = new KpiDetails();
            while (!(rdr.NodeType == XmlNodeType.EndElement
                    && rdr.LocalName == "Kpi"))
            {
                while (rdr.MoveToNextAttribute())
                    {
                        if (rdr.LocalName == "StatusGraphic")
                        {
                                kpi.Graphic = rdr.Value;
                        }
                    }
                if (rdr.NodeType == XmlNodeType.Element
                    && rdr.LocalName == "KpiGoal")
                {
                    while (!(rdr.NodeType == XmlNodeType.EndElement
                    && rdr.LocalName == "KpiGoal"))
                    {
                        if (rdr.NodeType == XmlNodeType.Element
                            && rdr.LocalName == "PropertyRef")
                        {
                            while (rdr.MoveToNextAttribute())
                            {
                                if (rdr.LocalName == "Name")
                                {
                                    kpi.Goal = rdr.Value;
                                }
                            }
                        }
                        rdr.Read();
                    }
                }


                if (rdr.NodeType == XmlNodeType.Element
                    && rdr.LocalName == "KpiStatus")
                {
                    while (!(rdr.NodeType == XmlNodeType.EndElement
                    && rdr.LocalName == "KpiStatus"))
                    {
                        if (rdr.NodeType == XmlNodeType.Element
                            && rdr.LocalName == "PropertyRef")
                        {
                            while (rdr.MoveToNextAttribute())
                            {
                                if (rdr.LocalName == "Name")
                                {
                                    kpi.Status = rdr.Value;
                                }
                            }
                        }
                        rdr.Read();
                    }
                }

                rdr.Read();
            }
            return kpi;
        }

        private void ProcessHierarchy(XmlReader rdr, ADOTabularTable table,string eEntityType)
        {
            var hierName = "";
            var hierHidden = false;
            ADOTabularHierarchy hier = null;
            ADOTabularLevel lvl = null;
            string lvlName = "";
            string lvlCaption = "";
            string lvlRef = "";
            
            while (!(rdr.NodeType == XmlNodeType.EndElement
                    && rdr.LocalName == "Hierarchy"))
            {
                if (rdr.NodeType == XmlNodeType.Element
                    && rdr.LocalName == "Hierarchy")
                {
                    while (rdr.MoveToNextAttribute())
                    {
                        switch (rdr.LocalName)
                        {
                            case "Hidden":
                                hierHidden = bool.Parse(rdr.Value);
                                break;
                            case "Name":
                                hierName = rdr.Value;
                                    
                                break;
                        }
                    }
                    hier = new ADOTabularHierarchy(table, hierName,hierName, "", hierHidden, ADOTabularColumnType.Hierarchy, "");
                    table.Columns.Add(hier);
                    rdr.Read();
                }

                while (!(rdr.NodeType == XmlNodeType.EndElement
                    && rdr.LocalName == "Level"))
                {
                    if ((rdr.NodeType == XmlNodeType.Element)
                        && (rdr.LocalName == "Level"))
                    {
                        while (rdr.MoveToNextAttribute())
                        {
                            switch (rdr.LocalName)
                            {
                                case "Name":
                                    lvlName = rdr.Value;
                                    break;
                                case "Caption":
                                    lvlCaption = rdr.Value;
                                    break;
                            }
                        }
                    }

                    if ((rdr.NodeType == XmlNodeType.Element)
                        && (rdr.LocalName == "PropertyRef"))
                    {
                        while (rdr.MoveToNextAttribute())
                        {
                            switch (rdr.LocalName)
                            {
                                case "Name":
                                    lvlRef = rdr.Value;
                                    break;
                            }
                        }
                    }

                    rdr.Read();
                } //End of Level
                    
                lvl = new ADOTabularLevel(table.Columns[lvlRef]);
                lvl.LevelName = lvlName;
                lvl.LevelCaption = lvlCaption;
                hier.Levels.Add(lvl);
                lvlName = "";
                lvlCaption = "";
                lvlRef = "";
                while ( true )
                {
                    if (rdr.NodeType == XmlNodeType.Element && rdr.LocalName == "Level") break;
                    if (rdr.NodeType == XmlNodeType.EndElement && rdr.LocalName == "Hierarchy") break;
                    rdr.Read();
                } 
            }
             
        }

        SortedDictionary<string, ADOTabularColumn> IMetaDataVisitor.Visit(ADOTabularColumnCollection columns)
        {
            return new SortedDictionary<string, ADOTabularColumn>();
        }

        public void Visit(ADOTabularFunctionGroupCollection functionGroups)
        {
            DataRow[] drFuncs = _conn.GetSchemaDataSet("MDSCHEMA_FUNCTIONS").Tables[0].Select("ORIGIN=3 OR ORIGIN=4");
            foreach (DataRow dr in drFuncs)
            {
                functionGroups.AddFunction(dr);
            }
        }


    }

}