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
using System.Linq.Expressions;

namespace Microsoft.Scripting.Actions {
    public class BoundMemberTracker : MemberTracker {
        private Expression _instance;
        private MemberTracker _tracker;
        private object _objInst;

        public BoundMemberTracker(MemberTracker tracker, Expression instance) {
            _tracker = tracker;
            _instance = instance;
        }

        public BoundMemberTracker(MemberTracker tracker, object instance) {
            _tracker = tracker;
            _objInst = instance;
        }

        public override TrackerTypes MemberType {
            get { return TrackerTypes.Bound; }
        }

        public override Type DeclaringType {
            get { return _tracker.DeclaringType; }
        }

        public override string Name {
            get { return _tracker.Name; }
        }

        public Expression Instance {
            get {
                return _instance;
            }
        }

        public object ObjectInstance {
            get {
                return _objInst;
            }
        }

        public MemberTracker BoundTo {
            get {
                return _tracker;
            }
        }

        public override Expression GetValue(Expression context, ActionBinder binder, Type type) {
            return _tracker.GetBoundValue(context, binder, type, _instance);
        }

        public override ErrorInfo GetError(ActionBinder binder) {
            return _tracker.GetBoundError(binder, _instance);
        }

        public override Expression SetValue(Expression context, ActionBinder binder, Type type, Expression value) {
            return _tracker.SetBoundValue(context, binder, type, value, _instance);
        }
    }
}
