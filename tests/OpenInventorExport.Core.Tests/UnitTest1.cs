using System.Diagnostics;

namespace OpenInventorExport.Core.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            ComponentRecord componentRecord = new ComponentRecord();
            componentRecord.FileName = "test.txt";

            if (componentRecord.FileName != "")
            {
                Debug.WriteLine($"FileName is set to: {componentRecord.FileName}");
            }
        }
    }
}
