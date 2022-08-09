using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Server_Test
{
    public class SerializationPerformances
    {
        public void TestCustomObjectSerialization()
        {
            NetworkMessage message = new NetworkMessage();
            message.SetObject<GameClient>(new GameClient());
            var data = message.Serialize();
            NetworkMessage msg = new NetworkMessage();
            msg.SetData(data);
            GameClient go = msg.GetObject<GameClient>();
        }

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

        public unsafe void TestArrayPerfo(int nbtests)
        {
            FieldInfo field = typeof(List<byte[]>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
            List<byte[]> blocks = new List<byte[]>();
            List<byte> bytes = new List<byte>();
            byte[] finalDirect = new byte[0];
            byte[] blocksToAdd = System.Text.Encoding.UTF8.GetBytes("this is a byte array manipulation performances test");
            Stopwatch sw = new Stopwatch();

            // test blocks
            sw.Start();
            for (int i = 0; i < nbtests; i++)
                blocks.Add(blocksToAdd);
            byte[] final1 = new byte[blocks.Count * blocksToAdd.Length];
            for (int i = 0; i < nbtests; i++)
                Buffer.BlockCopy(blocks[i], 0, final1, i * blocksToAdd.Length, blocksToAdd.Length);
            sw.Stop();
            Console.WriteLine("List of bytes v1 duration : " + sw.ElapsedMilliseconds + " ms");

            blocks = new List<byte[]>();
            // test blocks
            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbtests; i++)
                blocks.Add(blocksToAdd);
            byte[][] jagged = field.GetValue(blocks) as byte[][];
            byte[] final_2 = new byte[blocks.Count * blocksToAdd.Length];
            for (int i = 0; i < nbtests; i++)
                Buffer.BlockCopy(jagged[i], 0, final_2, i * blocksToAdd.Length, blocksToAdd.Length);

            //fixed(byte* tmpPtr = tmp[0])
            //{
            //    fixed (byte* tmpPtr2 = final1_2)
            //        Buffer.MemoryCopy(tmpPtr, tmpPtr2, final1_2.Length, final1_2.Length);
            //        //tmpPtr2 = tmpPtr;
            //}
            sw.Stop();
            Console.WriteLine("List of bytes v2 duration : " + sw.ElapsedMilliseconds + " ms");

            // test Bytes
            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbtests; i++)
                bytes.AddRange(blocksToAdd);
            byte[] final2 = bytes.ToArray();
            sw.Stop();
            Console.WriteLine("List of byte duration : " + sw.ElapsedMilliseconds + " ms");

            // test Bytes
            sw.Reset();
            sw.Start();
            for (int i = 0; i < nbtests; i++)
                for (int j = 0; j < blocksToAdd.Length; j++)
                    bytes.Add(blocksToAdd[j]);
            byte[] final2_2 = bytes.ToArray();
            sw.Stop();
            Console.WriteLine("List of byte v2 duration : " + sw.ElapsedMilliseconds + " ms");

            // test Bytes
            sw.Reset();
            sw.Start();
            finalDirect = new byte[0];
            for (int i = 0; i < nbtests; i++)
            {
                Array.Resize<byte>(ref finalDirect, finalDirect.Length + blocksToAdd.Length);
                Buffer.BlockCopy(blocksToAdd, 0, finalDirect, i * blocksToAdd.Length, blocksToAdd.Length);
            }
            sw.Stop();
            Console.WriteLine("Resize array duration : " + sw.ElapsedMilliseconds + " ms");

        }

        public void TestContainPerfo()
        {
            HashSet<int> intSet = new HashSet<int>();
            HashSet<uint> uintSet = new HashSet<uint>();
            HashSet<string> stringSet = new HashSet<string>();
            for (int i = 0; i < 1000000; i++)
            {
                intSet.Add(i);
                uintSet.Add((uint)i);
                stringSet.Add(i.ToString());
            }
            Stopwatch sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < 1000000; i++)
                intSet.Contains(i);
            sw.Stop();
            Console.WriteLine("int : " + sw.ElapsedMilliseconds);
            sw.Reset();
            sw.Start();

            for (uint i = 0; i < 1000000; i++)
                uintSet.Contains(i);
            sw.Stop();
            Console.WriteLine("uint : " + sw.ElapsedMilliseconds);
            sw.Reset();
            sw.Start();

            for (int i = 0; i < 1000000; i++)
                stringSet.Contains("1");
            sw.Stop();
            Console.WriteLine("string : " + sw.ElapsedMilliseconds);
            sw.Reset();
        }

        public void HashSetContainsPerfo()
        {
            HashSet<int> intSet = new HashSet<int>();
            for (int i = 0; i < 1000000; i++)
                if (intSet.Add(i)) { }

            Stopwatch sw = new Stopwatch();

            sw.Start();
            for (int i = 0; i < 1000000; i++)
                intSet.Contains(i);
            sw.Stop();
            Console.WriteLine("int : " + sw.ElapsedMilliseconds);
            sw.Reset();
            sw.Start();


            sw.Start();
            for (int i = 0; i < 1000000; i++)
            {
                intSet.Add(i);
            }
            sw.Stop();
            Console.WriteLine("int : " + sw.ElapsedMilliseconds);
            sw.Reset();
            sw.Start();
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

    [Serializable]
    public class GameClient
    {
        public int ID { get; set; }
        public string Account { get; set; }
        public string Pseudo { get; set; }
        public string Pass { get; set; }
        public string Mail { get; set; }
        public bool isInFight { get; set; }
        public ushort mapID { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public int Level { get; set; }
        public int XP { get; set; }
    }
}