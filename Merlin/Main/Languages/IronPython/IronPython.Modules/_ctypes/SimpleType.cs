/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Scripting;
using Microsoft.Scripting.Math;
using Microsoft.Scripting.Runtime;

using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

#if !SILVERLIGHT

namespace IronPython.Modules {
    /// <summary>
    /// Provides support for interop with native code from Python code.
    /// </summary>
    public static partial class CTypes {
       
        /// <summary>
        /// The meta class for ctypes simple data types.  These include primitives like ints,
        /// floats, etc... char/wchar pointers, and untyped pointers.
        /// </summary>
        [PythonType, PythonHidden]
        public class SimpleType : PythonType, INativeType {
            internal readonly SimpleTypeKind _type;
            private readonly char _charType;

            public SimpleType(CodeContext/*!*/ context, string name, PythonTuple bases, IAttributesCollection dict)
                : base(context, name, bases, dict) {
                object val;
                string sVal;

                const string allowedTypes = "?cbBghHiIlLdfuzZqQPXOv";
                if (!TryGetBoundCustomMember(context, SymbolTable.StringToId("_type_"), out val) ||
                    (sVal = StringOps.AsString(val)) == null ||
                    sVal.Length != 1 ||
                    allowedTypes.IndexOf(sVal[0]) == -1) {
                    throw PythonOps.AttributeError("AttributeError: class must define a '_type_' attribute which must be a single character string containing one of '{0}'.", allowedTypes);
                }

                _charType = sVal[0];
                switch (sVal[0]) {
                    case '?': _type = SimpleTypeKind.Boolean; break;
                    case 'c': _type = SimpleTypeKind.Char; break;
                    case 'b': _type = SimpleTypeKind.SignedByte; break;
                    case 'B': _type = SimpleTypeKind.UnsignedByte; break;
                    case 'h': _type = SimpleTypeKind.SignedShort; break;
                    case 'H': _type = SimpleTypeKind.UnsignedShort; break;
                    case 'i': _type = SimpleTypeKind.SignedInt; break;
                    case 'I': _type = SimpleTypeKind.UnsignedInt; break;
                    case 'l': _type = SimpleTypeKind.SignedLong; break;
                    case 'L': _type = SimpleTypeKind.UnsignedLong; break;
                    case 'f': _type = SimpleTypeKind.Single; break;
                    case 'g': // long double, new in 2.6
                    case 'd': _type = SimpleTypeKind.Double; break;
                    case 'q': _type = SimpleTypeKind.SignedLongLong; break;
                    case 'Q': _type = SimpleTypeKind.UnsignedLongLong; break;
                    case 'O': _type = SimpleTypeKind.Object; break;
                    case 'P': _type = SimpleTypeKind.Pointer; break;
                    case 'z': _type = SimpleTypeKind.CharPointer; break;
                    case 'Z': _type = SimpleTypeKind.WCharPointer; break;
                    case 'u': _type = SimpleTypeKind.WChar; break;
                    case 'p': // what are these?
                    case 'X':
                    case 'v':
                        throw new NotImplementedException("simple type " + sVal);
                }
            }

            private SimpleType(Type underlyingSystemType)
                : base(underlyingSystemType) {
            }

            public static ArrayType/*!*/ operator *(SimpleType type, int count) {
                return MakeArrayType(type, count);
            }

            public static ArrayType/*!*/ operator *(int count, SimpleType type) {
                return MakeArrayType(type, count);
            }

            internal static PythonType MakeSystemType(Type underlyingSystemType) {
                return PythonType.SetPythonType(underlyingSystemType, new SimpleType(underlyingSystemType));
            }

            /// <summary>
            /// Converts an object into a function call parameter.
            /// </summary>
            public object from_param(object obj) {
                // TODO: This isn't right as we have an obj associated w/ the argument, CPython doesn't.

                return new NativeArgument((CData)PythonCalls.Call(this, obj), _charType.ToString());
            }

            public SimpleCData from_address(CodeContext/*!*/ context, int address) {
                return from_address(context, new IntPtr(address));
            }

            public SimpleCData from_address(CodeContext/*!*/ context, BigInteger address) {
                return from_address(context, new IntPtr(address.ToInt64()));
            }

            public SimpleCData from_address(CodeContext/*!*/ context, IntPtr ptr) {
                SimpleCData res = (SimpleCData)CreateInstance(context);
                res.SetAddress(ptr);
                return res;
            }

            public SimpleCData in_dll(CodeContext/*!*/ context, object library, string name) {
                IntPtr handle = GetHandleFromObject(library, "in_dll expected object with _handle attribute");
                IntPtr addr = NativeFunctions.GetProcAddress(handle, name);
                if (addr == IntPtr.Zero) {
                    throw PythonOps.ValueError("{0} not found when attempting to load {1} from dll", name, Name);
                }

                SimpleCData res = (SimpleCData)CreateInstance(context);
                res.SetAddress(addr);
                return res;
            }

            #region INativeType Members

            int INativeType.Size {
                get {
                    switch (_type) {
                        case SimpleTypeKind.Char:
                        case SimpleTypeKind.SignedByte:
                        case SimpleTypeKind.UnsignedByte:
                        case SimpleTypeKind.Boolean:
                            return 1;
                        case SimpleTypeKind.SignedShort:
                        case SimpleTypeKind.UnsignedShort:
                        case SimpleTypeKind.WChar:
                            return 2;
                        case SimpleTypeKind.SignedInt:
                        case SimpleTypeKind.UnsignedInt:
                        case SimpleTypeKind.UnsignedLong:
                        case SimpleTypeKind.SignedLong:
                        case SimpleTypeKind.Single:
                            return 4;
                        case SimpleTypeKind.Double:
                        case SimpleTypeKind.UnsignedLongLong:
                        case SimpleTypeKind.SignedLongLong:
                            return 8;
                        case SimpleTypeKind.Object:
                        case SimpleTypeKind.Pointer:
                        case SimpleTypeKind.CharPointer:
                        case SimpleTypeKind.WCharPointer:
                            return IntPtr.Size;
                    }
                    throw new InvalidOperationException(_type.ToString());
                }
            }

            int INativeType.Alignment {
                get {
                    return ((INativeType)this).Size;
                }
            }

            object INativeType.GetValue(MemoryHolder/*!*/ owner, int offset, bool raw) {
                object res;
                switch (_type) {
                    case SimpleTypeKind.Boolean: res = owner.ReadByte(offset) != 0 ? ScriptingRuntimeHelpers.True : ScriptingRuntimeHelpers.False; break;
                    case SimpleTypeKind.Char: res = new string((char)owner.ReadByte(offset), 1); break;
                    case SimpleTypeKind.SignedByte: res = GetIntReturn((int)(sbyte)owner.ReadByte(offset)); break;
                    case SimpleTypeKind.UnsignedByte: res = GetIntReturn((int)owner.ReadByte(offset)); break;
                    case SimpleTypeKind.SignedShort: res = GetIntReturn((int)owner.ReadInt16(offset)); break;
                    case SimpleTypeKind.WChar: res = new string((char)owner.ReadInt16(offset), 1); break;
                    case SimpleTypeKind.UnsignedShort: res = GetIntReturn((int)(ushort)owner.ReadInt16(offset)); break;
                    case SimpleTypeKind.SignedInt: res = GetIntReturn((int)owner.ReadInt32(offset)); break;
                    case SimpleTypeKind.UnsignedInt: res = GetIntReturn((uint)owner.ReadInt32(offset)); break;
                    case SimpleTypeKind.UnsignedLong: res = GetIntReturn((uint)owner.ReadInt32(offset)); break;
                    case SimpleTypeKind.SignedLong: res = GetIntReturn(owner.ReadInt32(offset)); break;
                    case SimpleTypeKind.Single: res = GetSingleReturn(owner.ReadInt32(offset)); break;
                    case SimpleTypeKind.Double: res = GetDoubleReturn(owner.ReadInt64(offset)); break;
                    case SimpleTypeKind.UnsignedLongLong: res = GetIntReturn((ulong)owner.ReadInt64(offset)); break;
                    case SimpleTypeKind.SignedLongLong: res = GetIntReturn(owner.ReadInt64(offset)); break;
                    case SimpleTypeKind.Object: res = GetObjectReturn(owner.ReadIntPtr(offset)); break;
                    case SimpleTypeKind.Pointer: res = ToPython(owner.ReadIntPtr(offset)); break;
                    case SimpleTypeKind.CharPointer: res = owner.ReadAnsiString(offset); break;
                    case SimpleTypeKind.WCharPointer: res = owner.ReadUnicodeString(offset); break;
                    default:
                        throw new InvalidOperationException();
                }

                if (!raw && IsSubClass) {
                    res = PythonCalls.Call(this, res);
                }

                return res;
            }

            /// <summary>
            /// Helper function for reading char/wchar's.  This is used for reading from
            /// arrays and pointers to avoid creating lots of 1-char strings.
            /// </summary>
            internal char ReadChar(MemoryHolder/*!*/ owner, int offset) {
                switch (_type) {
                    case SimpleTypeKind.Char: return (char)owner.ReadByte(offset);
                    case SimpleTypeKind.WChar: return (char)owner.ReadInt16(offset);
                    default: throw new InvalidOperationException();
                }
            }

            void INativeType.SetValue(MemoryHolder/*!*/ owner, int offset, object value) {
                SimpleCData data = value as SimpleCData;
                if (data != null && data.NativeType == this) {
                    data._memHolder.CopyTo(owner, offset, ((INativeType)this).Size);
                    return;
                }

                switch (_type) {
                    case SimpleTypeKind.Boolean: owner.WriteByte(offset, ModuleOps.GetBoolean(value)); break;
                    case SimpleTypeKind.Char: owner.WriteByte(offset, ModuleOps.GetChar(value)); break;
                    case SimpleTypeKind.SignedByte: owner.WriteByte(offset, ModuleOps.GetSignedByte(value)); break;
                    case SimpleTypeKind.UnsignedByte: owner.WriteByte(offset, ModuleOps.GetUnsignedByte(value)); break;
                    case SimpleTypeKind.WChar: owner.WriteInt16(offset, (short)ModuleOps.GetWChar(value)); break;
                    case SimpleTypeKind.SignedShort: owner.WriteInt16(offset, ModuleOps.GetSignedShort(value)); break;
                    case SimpleTypeKind.UnsignedShort: owner.WriteInt16(offset, ModuleOps.GetUnsignedShort(value)); break;
                    case SimpleTypeKind.SignedInt: owner.WriteInt32(offset, ModuleOps.GetSignedInt(value)); break;
                    case SimpleTypeKind.UnsignedInt: owner.WriteInt32(offset, ModuleOps.GetUnsignedInt(value)); break;
                    case SimpleTypeKind.UnsignedLong: owner.WriteInt32(offset, ModuleOps.GetUnsignedLong(value)); break;
                    case SimpleTypeKind.SignedLong: owner.WriteInt32(offset, ModuleOps.GetSignedLong(value)); break;
                    case SimpleTypeKind.Single: owner.WriteInt32(offset, ModuleOps.GetSingleBits(value)); break;
                    case SimpleTypeKind.Double: owner.WriteInt64(offset, ModuleOps.GetDoubleBits(value)); break;
                    case SimpleTypeKind.UnsignedLongLong: owner.WriteInt64(offset, ModuleOps.GetUnsignedLongLong(value)); break;
                    case SimpleTypeKind.SignedLongLong: owner.WriteInt64(offset, ModuleOps.GetSignedLongLong(value)); break;
                    case SimpleTypeKind.Object: owner.WriteIntPtr(offset, ModuleOps.GetObject(value)); break;
                    case SimpleTypeKind.Pointer: owner.WriteIntPtr(offset, ModuleOps.GetPointer(value)); break;
                    case SimpleTypeKind.CharPointer: owner.WriteIntPtr(offset, ModuleOps.GetCharPointer(value)); break;
                    case SimpleTypeKind.WCharPointer: owner.WriteIntPtr(offset, ModuleOps.GetWCharPointer(value)); break;
                    default:
                        throw new InvalidOperationException();
                }
            }

            Type/*!*/ INativeType.GetNativeType() {
                switch (_type) {
                    case SimpleTypeKind.Boolean:
                        return typeof(bool);
                    case SimpleTypeKind.Char:
                        return typeof(byte);
                    case SimpleTypeKind.SignedByte:
                        return typeof(sbyte);
                    case SimpleTypeKind.UnsignedByte:
                        return typeof(byte);
                    case SimpleTypeKind.SignedShort:
                        return typeof(short);
                    case SimpleTypeKind.UnsignedShort:
                        return typeof(ushort);
                    case SimpleTypeKind.WChar:
                        return typeof(char);
                    case SimpleTypeKind.SignedInt:
                    case SimpleTypeKind.SignedLong:
                        return typeof(int);
                    case SimpleTypeKind.UnsignedInt:
                    case SimpleTypeKind.UnsignedLong:
                        return typeof(uint);
                    case SimpleTypeKind.Single:
                        return typeof(float);
                    case SimpleTypeKind.Double:
                        return typeof(double);
                    case SimpleTypeKind.UnsignedLongLong:
                        return typeof(ulong);
                    case SimpleTypeKind.SignedLongLong:
                        return typeof(long);
                    case SimpleTypeKind.Object:
                        return typeof(IntPtr);
                    case SimpleTypeKind.Pointer:
                    case SimpleTypeKind.CharPointer:
                    case SimpleTypeKind.WCharPointer:
                        return typeof(IntPtr);
                }

                throw new InvalidOperationException();
            }

            MarshalCleanup INativeType.EmitMarshalling(ILGenerator/*!*/ method, LocalOrArg argIndex, List<object>/*!*/ constantPool, int constantPoolArgument) {
                MarshalCleanup cleanup = null;
                Label marshalled = method.DefineLabel();
                Type argumentType = argIndex.Type;

                if (!argumentType.IsValueType) {
                    // check if we have an explicit CData instance.  If we have a CData but it's the
                    // wrong type CheckSimpleCDataType will throw.
                    Label primitive = method.DefineLabel();

                    argIndex.Emit(method);

                    constantPool.Add(this);
                    method.Emit(OpCodes.Ldarg, constantPoolArgument);
                    method.Emit(OpCodes.Ldc_I4, constantPool.Count - 1);
                    method.Emit(OpCodes.Ldelem_Ref);
                    method.Emit(OpCodes.Call, typeof(ModuleOps).GetMethod("CheckSimpleCDataType"));
                    method.Emit(OpCodes.Brfalse, primitive);

                    argIndex.Emit(method);
                    method.Emit(OpCodes.Castclass, typeof(CData));
                    method.Emit(OpCodes.Call, typeof(CData).GetMethod("get_UnsafeAddress"));
                    method.Emit(OpCodes.Ldobj, ((INativeType)this).GetNativeType());
                    method.Emit(OpCodes.Br, marshalled);

                    method.MarkLabel(primitive);
                }

                argIndex.Emit(method);
                if (argumentType.IsValueType) {
                    method.Emit(OpCodes.Box, argumentType);
                }
                switch (_type) {
                    case SimpleTypeKind.Boolean:
                    case SimpleTypeKind.Char:
                    case SimpleTypeKind.SignedByte:
                    case SimpleTypeKind.UnsignedByte:
                    case SimpleTypeKind.SignedShort:
                    case SimpleTypeKind.UnsignedShort:
                    case SimpleTypeKind.WChar:
                    case SimpleTypeKind.SignedInt:
                    case SimpleTypeKind.UnsignedInt:
                    case SimpleTypeKind.UnsignedLong:
                    case SimpleTypeKind.SignedLong:
                    case SimpleTypeKind.Single:
                    case SimpleTypeKind.Double:
                    case SimpleTypeKind.UnsignedLongLong:
                    case SimpleTypeKind.SignedLongLong:
                    case SimpleTypeKind.Pointer:
                        method.Emit(OpCodes.Call, typeof(ModuleOps).GetMethod("Get" + _type));
                        break;
                    case SimpleTypeKind.Object:
                        // TODO: Need cleanup here
                        method.Emit(OpCodes.Call, typeof(CTypes).GetMethod("PyObj_ToPtr"));
                        break;
                    case SimpleTypeKind.CharPointer:
                        Label isNull = method.DefineLabel();
                        Label done = method.DefineLabel();
                        method.Emit(OpCodes.Brfalse, isNull);
                        argIndex.Emit(method);
                        if (argumentType.IsValueType) {
                            method.Emit(OpCodes.Box, argumentType);
                        }

                        LocalBuilder lb = method.DeclareLocal(typeof(IntPtr));
                        method.Emit(OpCodes.Call, typeof(ModuleOps).GetMethod("StringToHGlobalAnsi"));
                        method.Emit(OpCodes.Stloc, lb);
                        method.Emit(OpCodes.Ldloc, lb);
                        method.Emit(OpCodes.Br, done);

                        method.MarkLabel(isNull);
                        method.Emit(OpCodes.Ldc_I4_0);
                        method.Emit(OpCodes.Conv_I);

                        method.MarkLabel(done);
                        cleanup = new StringCleanup(lb);
                        break;
                    case SimpleTypeKind.WCharPointer:
                        isNull = method.DefineLabel();
                        done = method.DefineLabel();
                        method.Emit(OpCodes.Brfalse, isNull);
                        argIndex.Emit(method);
                        if (argumentType.IsValueType) {
                            method.Emit(OpCodes.Box, argumentType);
                        }

                        lb = method.DeclareLocal(typeof(string), true);
                        method.Emit(OpCodes.Stloc, lb);
                        method.Emit(OpCodes.Ldloc, lb);
                        method.Emit(OpCodes.Conv_I);
                        method.Emit(OpCodes.Ldc_I4, RuntimeHelpers.OffsetToStringData);
                        method.Emit(OpCodes.Add);
                        method.Emit(OpCodes.Br, done);

                        method.MarkLabel(isNull);
                        method.Emit(OpCodes.Ldc_I4_0);
                        method.Emit(OpCodes.Conv_I);

                        method.MarkLabel(done);
                        break;
                }

                method.MarkLabel(marshalled);
                return cleanup;
            }

            Type/*!*/ INativeType.GetPythonType() {
                switch (_type) {
                    case SimpleTypeKind.Boolean:
                        return typeof(bool);
                    case SimpleTypeKind.CharPointer:
                    case SimpleTypeKind.WCharPointer:
                    case SimpleTypeKind.WChar:
                    case SimpleTypeKind.Char:
                        return typeof(string);
                    case SimpleTypeKind.SignedByte:
                    case SimpleTypeKind.UnsignedByte:
                    case SimpleTypeKind.SignedShort:
                    case SimpleTypeKind.UnsignedShort:
                    case SimpleTypeKind.SignedInt:
                    case SimpleTypeKind.SignedLong:
                        return typeof(int);
                    case SimpleTypeKind.UnsignedInt:
                    case SimpleTypeKind.UnsignedLong:
                    case SimpleTypeKind.UnsignedLongLong:
                    case SimpleTypeKind.SignedLongLong:
                    case SimpleTypeKind.Pointer:
                    case SimpleTypeKind.Object:
                        return typeof(object);
                    case SimpleTypeKind.Single:
                    case SimpleTypeKind.Double:
                        return typeof(double);
                    default:
                        throw new InvalidOperationException();
                }
            }

            void INativeType.EmitReverseMarshalling(ILGenerator method, LocalOrArg value, List<object> constantPool, int constantPoolArgument) {
                value.Emit(method);
                switch (_type) {
                    case SimpleTypeKind.SignedByte:
                    case SimpleTypeKind.UnsignedByte:
                    case SimpleTypeKind.SignedShort:
                    case SimpleTypeKind.UnsignedShort:
                        method.Emit(OpCodes.Conv_I4);
                        break;
                    case SimpleTypeKind.SignedInt:
                    case SimpleTypeKind.SignedLong:
                    case SimpleTypeKind.Boolean:
                        break;
                    case SimpleTypeKind.Single:
                        method.Emit(OpCodes.Conv_R8);
                        break;
                    case SimpleTypeKind.Double:
                        break;
                    case SimpleTypeKind.UnsignedLongLong:
                    case SimpleTypeKind.UnsignedInt:
                    case SimpleTypeKind.UnsignedLong:
                        EmtInt32ToObject(method, value);
                        break;
                    case SimpleTypeKind.SignedLongLong:
                        EmitInt64ToObject(method, value);
                        break;
                    case SimpleTypeKind.Object:
                        break;
                    case SimpleTypeKind.WCharPointer:
                        method.Emit(OpCodes.Call, typeof(Marshal).GetMethod("PtrToStringUni", new[] { typeof(IntPtr) }));
                        break;
                    case SimpleTypeKind.CharPointer:
                        method.Emit(OpCodes.Call, typeof(Marshal).GetMethod("PtrToStringAnsi", new[] { typeof(IntPtr) }));
                        break;
                    case SimpleTypeKind.Char:
                        method.Emit(OpCodes.Call, typeof(ModuleOps).GetMethod("CharToString"));
                        break;
                    case SimpleTypeKind.WChar:
                        method.Emit(OpCodes.Call, typeof(ModuleOps).GetMethod("WCharToString"));
                        break;
                    case SimpleTypeKind.Pointer:
                        if (IntPtr.Size == 4) {
                            LocalBuilder tmpLocal = method.DeclareLocal(typeof(uint));
                            method.Emit(OpCodes.Conv_U4);
                            method.Emit(OpCodes.Stloc, tmpLocal);
                            method.Emit(OpCodes.Ldloc, tmpLocal);
                            EmtInt32ToObject(method, new Local(tmpLocal));
                        } else {
                            LocalBuilder tmpLocal = method.DeclareLocal(typeof(long));
                            method.Emit(OpCodes.Conv_I8);
                            method.Emit(OpCodes.Stloc, tmpLocal);
                            method.Emit(OpCodes.Ldloc, tmpLocal);
                            EmtInt32ToObject(method, new Local(tmpLocal));
                        }
                        break;
                }
            }

            private static void EmitInt64ToObject(ILGenerator method, LocalOrArg value) {
                Label done;
                Label bigInt = method.DefineLabel();
                done = method.DefineLabel();
                method.Emit(OpCodes.Ldc_I4, Int32.MaxValue);
                method.Emit(OpCodes.Conv_I8);
                method.Emit(OpCodes.Bgt, bigInt);

                value.Emit(method);
                method.Emit(OpCodes.Ldc_I4, Int32.MinValue);
                method.Emit(OpCodes.Conv_I8);
                method.Emit(OpCodes.Blt, bigInt);

                value.Emit(method);
                method.Emit(OpCodes.Conv_I4);
                method.Emit(OpCodes.Box, typeof(int));
                method.Emit(OpCodes.Br, done);

                method.MarkLabel(bigInt);
                value.Emit(method);
                method.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", new[] { value.Type }));

                method.MarkLabel(done);
            }

            private static void EmtInt32ToObject(ILGenerator method, LocalOrArg value) {
                Label intVal, done;
                intVal = method.DefineLabel();
                done = method.DefineLabel();
                method.Emit(OpCodes.Ldc_I4, Int32.MaxValue);
                method.Emit(value.Type == typeof(uint) ? OpCodes.Conv_U4 : OpCodes.Conv_U8);
                method.Emit(OpCodes.Ble, intVal);

                value.Emit(method);
                method.Emit(OpCodes.Call, typeof(BigInteger).GetMethod("op_Implicit", new[] { value.Type }));
                method.Emit(OpCodes.Br, done);

                method.MarkLabel(intVal);
                value.Emit(method);
                method.Emit(OpCodes.Conv_I4);
                method.Emit(OpCodes.Box, typeof(int));

                method.MarkLabel(done);
            }

            private bool IsSubClass {
                get {
                    return BaseTypes.Count != 1 || BaseTypes[0] != CTypes._SimpleCData;
                }
            }

            private object GetObjectReturn(IntPtr intPtr) {
                GCHandle handle = GCHandle.FromIntPtr(intPtr);
                object res = handle.Target;

                // TODO: handle lifetime management

                return res;
            }

            private object GetDoubleReturn(long p) {
                return BitConverter.ToDouble(BitConverter.GetBytes(p), 0);
            }

            private object GetSingleReturn(int p) {
                return BitConverter.ToSingle(BitConverter.GetBytes(p), 0);
            }

            private static object GetIntReturn(int value) {
                return ScriptingRuntimeHelpers.Int32ToObject((int)value);
            }

            private static object GetIntReturn(uint value) {
                if (value > Int32.MaxValue) {
                    return (BigInteger)value;
                }

                return ScriptingRuntimeHelpers.Int32ToObject((int)value);
            }

            private static object GetIntReturn(long value) {
                if (value <= Int32.MaxValue && value >= Int32.MinValue) {
                    return (int)value;
                }

                return (BigInteger)value;
            }

            private static object GetIntReturn(ulong value) {
                if (value <= Int32.MaxValue) {
                    return (int)value;
                }

                return (BigInteger)value;
            }


            #endregion
        }
    }
}

#endif
