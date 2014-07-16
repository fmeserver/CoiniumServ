﻿#region License
// 
//     CoiniumServ - Crypto Currency Mining Pool Server Software
//     Copyright (C) 2013 - 2014, CoiniumServ Project - http://www.coinium.org
//     http://www.coiniumserv.com - https://github.com/CoiniumServ/CoiniumServ
// 
//     This software is dual-licensed: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//    
//     For the terms of this license, see licenses/gpl_v3.txt.
// 
//     Alternatively, you can license this software under a commercial
//     license or white-label it as set out in licenses/commercial.txt.
// 
#endregion

using System;
using Coinium.Mining.Shares;
using Coinium.Utils.Buffers;
using Coinium.Utils.Helpers.Time;

namespace Coinium.Mining.Vardiff
{
    public class VardiffManager:IVardiffManager
    {
        public IVardiffConfig Config { get; private set; }

        private float _variance;
        private int _bufferSize;
        private float _tMin;
        private float _tMax;

        public VardiffManager(IVardiffConfig vardiffConfig, IShareManager shareManager)
        {
            Config = vardiffConfig;

            if (!Config.Enabled)
                return;

            shareManager.ShareSubmitted += OnShare;
            
            _variance = vardiffConfig.TargetTime*((float)vardiffConfig.VariancePercent/100);
            _bufferSize = vardiffConfig.RetargetTime/vardiffConfig.TargetTime*4;
            _tMin = vardiffConfig.TargetTime - _variance;
            _tMax = vardiffConfig.TargetTime + _variance;
        }

        private void OnShare(object sender, EventArgs e)
        {
            var shareArgs = (ShareEventArgs) e;
            var miner = shareArgs.Miner;

            if (miner == null)
                return;

            var now = TimeHelpers.NowInUnixTime();

            if (miner.VardiffBuffer == null)
            {
                miner.LastVardiffRetarget = now - Config.RetargetTime / 2;
                miner.LastVardiffTimestamp = now;
                miner.VardiffBuffer = new RingBuffer(_bufferSize);
                return;
            }

            var sinceLast = now - miner.LastVardiffTimestamp; // how many seconds elapsed since last share?
            miner.VardiffBuffer.Append(sinceLast); // append it to vardiff buffer.
            miner.LastVardiffTimestamp = now;

            if (now - miner.LastVardiffRetarget < Config.RetargetTime && miner.VardiffBuffer.Size > 0) // check if we need a re-target.
                return;

            miner.LastVardiffRetarget = now;
            var average = miner.VardiffBuffer.Average;
            var deltaDiff = Config.TargetTime/average;

            if (average > _tMax && miner.Difficulty > Config.MinimumDifficulty)
            {
                if (deltaDiff*miner.Difficulty < Config.MinimumDifficulty)
                    deltaDiff = Config.MinimumDifficulty/miner.Difficulty;
            }
            else if (average < _tMin)
            {
                if (deltaDiff*miner.Difficulty > Config.MaximumDifficulty)
                    deltaDiff = Config.MaximumDifficulty/miner.Difficulty;
            }
            else
                return;

            miner.Difficulty = miner.Difficulty*deltaDiff;
            miner.SendDifficulty();
            miner.VardiffBuffer.Clear();
        }
    }
}
