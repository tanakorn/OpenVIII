﻿using Microsoft.Xna.Framework;
using System;

namespace OpenVIII.Fields.Scripts.Instructions
{
    /// <summary>
    /// Place this entity's model at XCoord, YCoord standing on the given walkmesh triangle. Unlike SET3, this function will place the event on the walkable terrain (the ZCoord is interpolated from the walkmesh).
    /// </summary>
    /// <see cref="http://wiki.ffrtt.ru/index.php?title=FF8/Field/Script/Opcodes/01D_SET"/>
    public sealed class SET : JsmInstruction
    {
        #region Fields

        private Vector2 _pos;
        private Int32 _walkmeshTriangleId;

        #endregion Fields

        #region Constructors

        public SET(Int32 walkmeshTriangleId, Int32 x, Int32 y) => (_walkmeshTriangleId, _pos.X, _pos.Y) = (walkmeshTriangleId, x, y);

        public SET(Int32 walkmeshTriangleId, IStack<IJsmExpression> stack)
            : this(walkmeshTriangleId,
                y: ((IConstExpression)stack.Pop()).Int32(),
                x: ((IConstExpression)stack.Pop()).Int32())
        {
        }

        #endregion Constructors

        #region Methods

        public override void Format(ScriptWriter sw, IScriptFormatterContext formatterContext, IServices services) => sw.Format(formatterContext, services)
                .Property(nameof(FieldObject.Model))
                .Method(nameof(FieldObjectModel.SetPosition))
                .Argument("walkmeshTriangleId", _walkmeshTriangleId)
                .Argument("x", (int)_pos.X)
                .Argument("y", (int)_pos.Y)
                .Comment(nameof(SET3));

        public override IAwaitable TestExecute(IServices services)
        {
            FieldObject currentObject = ServiceId.Field[services].Engine.CurrentObject;
            currentObject.Model.SetPosition(new WalkmeshCoords(_walkmeshTriangleId, _pos));
            return DummyAwaitable.Instance;
        }

        public override String ToString() => $"{nameof(SET)}({nameof(_walkmeshTriangleId)}: {_walkmeshTriangleId}, {nameof(_pos)}: {_pos})";

        #endregion Methods
    }
}