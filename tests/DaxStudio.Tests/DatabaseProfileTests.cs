﻿using ADOTabular.DatabaseProfile;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using DaxStudio.UI.Utils;

namespace DaxStudio.Tests
{
    [TestClass]
    public class DatabaseProfileTests
    {
        public string ConnectionString { get; private set; }

        [TestInitialize]
        public void Setup()
        {
            ConnectionString = @"Data Source=localhost\tab16;Initial Catalog=AdventureWorks2012";
        }

        [TestMethod]
        public void TestAdventureWorksProfile()
        {
            
            var cnn = new ADOTabular.ADOTabularConnection(ConnectionString , ADOTabular.AdomdClientWrappers.AdomdType.AnalysisServices);
            cnn.Open();
            var db = cnn.Database;
            var profile = DatabaseProfiler.Create(db);

            Assert.AreEqual(db.Name, profile.Name);
            Assert.AreEqual(17, profile.Tables.Count);

            JsonSerializer serializer = new JsonSerializer();
            //serializer.Converters.Add(new JavaScriptDateTimeConverter());
            serializer.NullValueHandling = NullValueHandling.Ignore;

            using (StreamWriter sw = new StreamWriter(@"d:\temp\profile.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, profile);
                // {"ExpiryDate":new Date(1230375600000),"Price":0}
            }

            cnn.Close();
        }

        [TestMethod]
        public void TestVertipaqAnalyzerProfile()
        {
            //var cnnStr = ConnectionString;
            //var cnnStr = @"Data Source=http://localhost:9000/xmla;Workstation ID=C:\Users\c950497\Downloads\RAD Model.xlsx";
            var cnnStr = @"Data Source=localhost:9134;";
            var cnn = new ADOTabular.ADOTabularConnection(cnnStr, ADOTabular.AdomdClientWrappers.AdomdType.AnalysisServices);
            cnn.Open();
            var db = cnn.Database;
            var profile =  ModelAnalyzer.Create(cnn);

            

            JsonSerializer serializer = new JsonSerializer();
            //serializer.Converters.Add(new JavaScriptDateTimeConverter());
            serializer.NullValueHandling = NullValueHandling.Ignore;

            using (StreamWriter sw = new StreamWriter(@"d:\temp\BUSINESS_NBN_CUBE_VertipaqAnalyzerProfile.json"))
            using (JsonWriter writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, profile);
                // {"ExpiryDate":new Date(1230375600000),"Price":0}
            }

            cnn.Close();
        }

    }
}
