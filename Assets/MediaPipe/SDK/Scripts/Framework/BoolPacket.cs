using System;

namespace Mediapipe {
  public class BoolPacket : Packet<bool> {
    public BoolPacket() : base() {}

    public BoolPacket(IntPtr ptr, bool isOwner = true) : base(ptr, isOwner) {}

    public BoolPacket(bool value) : base() {
      UnsafeNativeMethods.mp__MakeBoolPacket__b(value, out var ptr).Assert();
      this.ptr = ptr;
    }

    public BoolPacket(bool value, Timestamp timestamp) : base() {
      UnsafeNativeMethods.mp__MakeBoolPacket_At__b_Rtimestamp(value, timestamp.mpPtr, out var ptr).Assert();
      this.ptr = ptr;
    }

    /// <exception cref="MediaPipeException">Thrown when the value is not set</exception>
    public override bool Get() {
      UnsafeNativeMethods.mp_Packet__GetBool(mpPtr, out var value).Assert();

      GC.KeepAlive(this);
      return value;
    }

    public override bool Consume() {
      throw new NotSupportedException();
    }

    public override Status ValidateAsType() {
      UnsafeNativeMethods.mp_Packet__ValidateAsBool(mpPtr, out var statusPtr);

      GC.KeepAlive(this);
      return new Status(statusPtr);
    }
  }
}
