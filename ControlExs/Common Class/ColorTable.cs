// ------------------------------------------------------------------
// Copyright (C) 2011-2016 Maruko Toolbox Project
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
// express or implied.
// See the License for the specific language governing permissions
// and limitations under the License.
// -------------------------------------------------------------------
//

using System;
using System.Collections.Generic;

using System.Text;
using System.Drawing;

namespace ControlExs
{
    /****************************************************************
    * 
    *           Author：苦笑
    *             Blog: http://www.cnblogs.com/Keep-Silence-/
    *             Date: 2013-2-22
    *
    *****************************************************************/

    /// <summary>
    /// 实现仿QQ效果控件内部使用颜色表
    /// </summary>
    public class ColorTable
    {
        public static Color QQBorderColor = Color.LightBlue;  //LightBlue = Color.FromArgb(173, 216, 230)
        public static Color QQHighLightColor =RenderHelper.GetColor(QQBorderColor,255,-63,-11,23);   //Color.FromArgb(110, 205, 253)
        public static Color QQHighLightInnerColor = RenderHelper.GetColor(QQBorderColor, 255, -100, -44, 1);   //Color.FromArgb(73, 172, 231);

        /// <summary>
        /// 由宿主程序（岚珠工具箱）在启动时调用，把控件配色切换为现代主题。
        /// 不调用则维持原有仿 QQ 蓝色外观，因此对其它引用 ControlExs 的项目无影响。
        /// </summary>
        public static void SetTheme(Color border, Color glowOuter, Color glowInner)
        {
            QQBorderColor = border;
            QQHighLightColor = glowOuter;
            QQHighLightInnerColor = glowInner;
        }
    }
}
