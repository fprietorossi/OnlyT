﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyT.Models
{
   /// <summary>
   /// Used when reading monitor device info from system
   /// </summary>
   public class DisplayDeviceData
   {
      public string Name { get; set; }
      public string DeviceId { get; set; }
      public string DeviceString { get; set; }
      public string DeviceKey { get; set; }
   }
}