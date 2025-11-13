// Based on Weak.cs

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Crawler;

[Serializable]
// This class is sealed to mitigate security issues caused by Object::MemberwiseClone.
public sealed partial class Weak<T> : ISerializable
    where T : class?
{
    private GCHandle _taggedHandle;

    // Creates a new Weak that keeps track of target.
    // Assumes a Short Weak Reference (ie TrackResurrection is false.)
    public Weak(T target)
        : this(target, false)
    {
    }

    // Creates a new Weak that keeps track of target.
    //
    public Weak(T target, bool trackResurrection)
    {
        Create(target, trackResurrection);
    }

    private Weak(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);

        T target = (T)info.GetValue("TrackedObject", typeof(T))!; // Do not rename (binary serialization)
        bool trackResurrection = info.GetBoolean("TrackResurrection"); // Do not rename (binary serialization)

        Create(target, trackResurrection);
    }

    //
    // We are exposing TryGetTarget instead of a simple getter to avoid a common problem where people write incorrect code like:
    //
    //      Weak ref = ...;
    //      if (ref.Target != null)
    //          DoSomething(ref.Target)
    //
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTarget([MaybeNullWhen(false), NotNullWhen(true)] out T target)
    {
        T? o = this.Target;
        target = o!;
        return o != null;
    }

//    [Obsolete(LegacyFormatterImplMessage, DiagnosticId = LegacyFormatterImplDiagId, UrlFormat = SharedUrlFormat)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);

        info.AddValue("TrackedObject", this.Target, typeof(T)); // Do not rename (binary serialization)
    }

    // Creates a new Weak that keeps track of target.
    private void Create(T target, bool trackResurrection) {
        _taggedHandle = GCHandle.Alloc(target, GCHandleType.Weak);

#if FEATURE_COMINTEROP || FEATURE_COMWRAPPERS
        ComAwareWeakReference.ComInfo? comInfo = ComAwareWeakReference.ComInfo.FromObject(target);
        if (comInfo != null)
        {
            ComAwareWeakReference.SetComInfoInConstructor(ref _taggedHandle, comInfo);
        }
#endif
    }

    public T? Target
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // Should only happen for corner cases, like using a Weak from a finalizer.
            // GC can finalize the instance if it becomes F-Reachable.
            // That, however, cannot happen while we use the instance.
            if (!_taggedHandle.IsAllocated)
                return default;


            T? target = Unsafe.As<T?>(_taggedHandle.Target);

            // must keep the instance alive as long as we use the handle.
            GC.KeepAlive(this);

            return target;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            // Should only happen for corner cases, like using a Weak from a finalizer.
            // GC can finalize the instance if it becomes F-Reachable.
            // That, however, cannot happen while we use the instance.
           if (!_taggedHandle.IsAllocated)
                throw new InvalidOperationException("Handle is not initialized.");

            _taggedHandle.Target = value;

            // must keep the instance alive as long as we use the handle.
            GC.KeepAlive(this);
        }
    }

#pragma warning disable CA1821 // Remove empty Finalizers
    ~Weak()
    {
        Debug.Fail(" Weak<T> finalizer should never run");
    }
#pragma warning restore CA1821 // Remove empty Finalizers

}
