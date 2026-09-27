using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace RazerBatteryTray.Macros
{
    public enum ActionKind { Delay, Keyboard, Mouse, CallMacro, Launch, Command, Text, LoopStart, LoopEnd }
    public enum PressMode { Tap, Down, Up }
    public enum MouseAction { Left, Right, Middle, X1, X2, WheelUp, WheelDown }
    public enum TriggerKind { Keyboard, Left, Right, Middle, X1, X2, WheelUp, WheelDown }
    public enum RunMode { Once, WhileHeld, Toggle }
    [Flags] public enum KeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Windows = 8 }

    public sealed class MacroStep
    {
        public ActionKind Kind { get; set; }
        public PressMode Press { get; set; }
        public int KeyCode { get; set; }
        public MouseAction Mouse { get; set; }
        public int Number { get; set; }
        public string Value { get; set; }
        public string Arguments { get; set; }
        public MacroStep() { Number = 100; KeyCode = 65; Value = ""; Arguments = ""; }
    }
    public sealed class MacroDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<MacroStep> Steps { get; set; }
        public MacroDefinition() { Id = Guid.NewGuid().ToString("N"); Name = "新宏"; Steps = new List<MacroStep>(); }
        public override string ToString() { return Name; }
    }
    public sealed class MacroBinding
    {
        public string Id { get; set; }
        public string MacroId { get; set; }
        public TriggerKind Trigger { get; set; }
        public int KeyCode { get; set; }
        public KeyModifiers Modifiers { get; set; }
        public RunMode Mode { get; set; }
        public bool SuppressOriginal { get; set; }
        public bool Enabled { get; set; }
        public MacroBinding() { Id = Guid.NewGuid().ToString("N"); MacroId = ""; KeyCode = 117; Enabled = true; }
        [XmlIgnore] public string Signature { get { return Trigger + ":" + (Trigger == TriggerKind.Keyboard ? KeyCode : 0) + ":" + (int)Modifiers; } }
    }
    [XmlRoot("LeiyunLiteMacros")]
    public sealed class MacroLibrary
    {
        public int Version { get; set; }
        public bool BindingsEnabled { get; set; }
        public List<MacroDefinition> Macros { get; set; }
        public List<MacroBinding> Bindings { get; set; }
        public MacroLibrary() { Version = 1; BindingsEnabled = true; Macros = new List<MacroDefinition>(); Bindings = new List<MacroBinding>(); }
        public MacroDefinition Find(string id) { return Macros.Find(m => m.Id == id); }
        public MacroLibrary Clone()
        {
            var serializer = new XmlSerializer(typeof(MacroLibrary));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, this); stream.Position = 0;
                return (MacroLibrary)serializer.Deserialize(stream);
            }
        }
    }
}
