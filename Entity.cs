using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SocketServer
{
    public class Entity
    {
        private int HP;
        private int Damage;
        private int Movement;
        private int Energy;

        public Entity(int hp, int damage, int movement = 5)
        {
            HP = hp;
            Damage = damage;
            Movement = movement;
            Energy = 100;
        }

        public void loseHP(int dmg)
        {
            HP -= dmg;
            if (HP < 0)
                HP = 0;
        }

        public void Attack(Entity other)
        {
            other.loseHP(Damage);
            Energy -= 30;
        }

        public int getHP() => HP;
        public int getDamage() => Damage;
        public int getMovement() => Movement;
        public int getEnergy() => Energy;

        public void takeEnergy(int value)
        {
            Energy -= value;
        }
        
        public void refilEnergy()
        {
            Energy = 100;
        }
        
        public void setHP(int value)
        {
            Energy = value;
        }
    }
}