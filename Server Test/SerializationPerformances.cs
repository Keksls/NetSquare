using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Server_Test
{
    public class SerializationPerformances
    {
        public void TestPerformances(int nbMessages)
        {
            Stopwatch sw = new Stopwatch();

            List<string> var1 = new List<string>() { "this", "is", "a", "list", "of", "string" };
            HashSet<double> var2 = new HashSet<double>() { double.MinValue, double.MaxValue };
            PerformanceTestClass var3 = new PerformanceTestClass();

            // mesure Array copy performances
            // memory copy
            byte[] array1 = System.Text.Encoding.UTF8.GetBytes("This is a test string for array 1");
            byte[] array2 = System.Text.Encoding.UTF8.GetBytes("This is a test string for array 2");
            NetworkMessage msgCopy = new NetworkMessage();

            // display compression efficiensy

            NetworkMessage msgLenght = new NetworkMessage();
            msgLenght.SetObject(var1);
            msgLenght.SetObject(var2);
            msgLenght.SetObject(var3);
            msgLenght.Serialize();
            int compressed = msgLenght.Data.Length;
            msgLenght.SetData(msgLenght.Data);
            int uncompressed = msgLenght.Data.Length;

            Console.WriteLine("Compressed : " + compressed + "  |  Uncompressed : " + uncompressed);

            // mesure creation
            sw.Start();
            for (int i = 0; i < nbMessages; i++)
            {
                NetworkMessage msg = new NetworkMessage();
                msg.SetObject(var1);
                msg.SetObject(var2);
                msg.SetObject(var3);
            }
            sw.Stop();
            Console.WriteLine("Creating " + nbMessages + " messages : " + sw.ElapsedMilliseconds + " ms");

            // Preparing for Serialization measurement
            List<NetworkMessage> messages = new List<NetworkMessage>();
            for (int i = 0; i < nbMessages; i++)
            {
                NetworkMessage msg = new NetworkMessage();
                msg.SetObject(var1);
                msg.SetObject(var2);
                msg.SetObject(var3);
                messages.Add(msg);
            }
            NetworkMessage[] messagesArray = messages.ToArray();

            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbMessages; i++)
            {
                messagesArray[i].Serialize();
            }
            sw.Stop();
            Console.WriteLine("Serializing " + nbMessages + " messages : " + sw.ElapsedMilliseconds + " ms");

            for (int i = 0; i < nbMessages; i++)
                messagesArray[i].SetData(messagesArray[i].Data);

            // deserializeing measurement
            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbMessages; i++)
            {
                messagesArray[i].GetObject<List<string>>();
                messagesArray[i].GetObject<HashSet<double>>();
                messagesArray[i].GetObject<PerformanceTestClass>();
            }
            Console.WriteLine("Deserializing " + nbMessages + " messages : " + sw.ElapsedMilliseconds + " ms");

            // full metrics
            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbMessages; i++)
            {
                NetworkMessage msg = new NetworkMessage();
                msg.SetObject(var1);
                msg.SetObject(var2);
                msg.SetObject(var3);
                msg.Serialize();
                msg.SetData(msg.Data);
                msg.GetObject<List<string>>();
                msg.GetObject<HashSet<double>>();
                msg.GetObject<PerformanceTestClass>();
            }
            Console.WriteLine("Full " + nbMessages + " messages : " + sw.ElapsedMilliseconds + " ms");
            Console.WriteLine("Serialized size : " + messagesArray[1].Data.Length);
        }
    }

    internal class PerformanceTestClass
    {
        public int ID;
        public string Name;
        public float Width;
        public float Height;
        public string Description;

        internal PerformanceTestClass()
        {
            ID = int.MaxValue;
            Name = "Test Class 1";
            Width = float.MaxValue;
            Height = float.MaxValue;
            Description = "This is a class that will be user for testing NetSquare NetworkMessage seralization. It will be serialized, sended over network and deserialize for reading";
        }
    }
}